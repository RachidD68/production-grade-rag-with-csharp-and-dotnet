# Runbook: Qdrant restore from blob snapshot

**Alert:** Qdrant data loss / corruption / collection missing -> page on-call (Sev-1 if retrieval is down).

The vector store is gone or corrupt and retrieval is returning empty or wrong results. Self-hosted Qdrant has no managed backup, so recovery means restoring the most recent snapshot from blob. Stop ingest first so you do not race the restore, recover the snapshot, verify counts, then resume. Move deliberately — a half-restored collection that serves traffic is worse than a brief read outage.

The snapshots are produced by the scheduled job in `deploy/modules/qdrant.bicep`, which calls the Qdrant snapshot API daily and uploads to the `qdrant-snapshots` blob container (`deploy/modules/storage.bicep`).

## DR posture

- **RPO ~24h** — snapshots run daily (`snapshotCron`, default 02:00 UTC). Worst case you lose up to one day of ingested documents; re-run ingest for that window after restore to close the gap. Tighten RPO by lowering the cron interval.
- **RTO ~30–60 min** — dominated by snapshot download + recover time, which scales with collection size. A multi-GB collection is the long pole.
- **Managed-HA alternative:** the `azure-search` backend (`deploy/modules/azureSearch.bicep`) replicates the index across replicas/partitions — no manual snapshots, no restore runbook, near-zero RPO. If this restore is happening more than rarely, that is the signal to switch `hybridBackend` to `azure-search`.

## Symptoms

- `ask` returns "no results" or wildly off-topic citations across all tenants.
- `qdrant.search` dependency calls fail, or the collection point count reads 0 / far below expected.
- The Qdrant container restarted onto an empty or corrupt volume.

## Diagnostic queries

```bash
# Is the collection present and how many points?
curl -s "http://<qdrant-host>:6333/collections/smartdocs" \
  | sed -n 's/.*"status":"\([a-z]*\)".*/\1/p'
curl -s "http://<qdrant-host>:6333/collections/smartdocs" \
  | sed -n 's/.*"points_count":\([0-9]*\).*/\1/p'

# Latest snapshots available in blob (most recent prefix wins)
az storage blob list \
  --account-name <snapshot-storage-account> --auth-mode login \
  -c qdrant-snapshots --query "[].name" -o tsv | sort | tail -20
```

## Procedure

### 1. Stop ingest

Pause the ingest worker so nothing writes during the restore.

```bash
# Scale the ingest worker Container App to zero
az containerapp update -g rg-smartdocs-prod -n ca-ingest-prod \
  --min-replicas 0 --max-replicas 0
```

Optionally put the API into read-degraded mode (feature flag) so users get a clear "temporarily unavailable" rather than empty answers:

```bash
az appconfig feature set --endpoint <appConfigEndpoint> --auth-mode login \
  --feature retrieval-maintenance --yes
```

### 2. Pull the snapshot from blob

Download the most recent good snapshot to a working dir.

```bash
SNAP=$(az storage blob list --account-name <snapshot-storage-account> \
  --auth-mode login -c qdrant-snapshots --query "[].name" -o tsv | sort | tail -1)

az storage blob download \
  --account-name <snapshot-storage-account> --auth-mode login \
  -c qdrant-snapshots -n "$SNAP" -f ./smartdocs.snapshot
```

### 3. Restore the snapshot to the ACI volume

Use the Qdrant recover API to rebuild the collection from the snapshot. Upload-and-recover is the supported path; for a full-volume corruption, place the `.snapshot` on the mounted file share and point recover at it.

```bash
# Place the downloaded snapshot on the mounted share, then recover from it.
# The snapshot dir on the persistent volume is /qdrant/storage/snapshots
# (QDRANT__STORAGE__SNAPSHOTS_PATH), matching the snapshot job in qdrant.bicep.
curl -X POST "http://<qdrant-host>:6333/collections/smartdocs/snapshots/recover" \
  -H "Content-Type: application/json" \
  -d '{"location":"file:///qdrant/storage/snapshots/smartdocs.snapshot"}'
```

If the container itself is unhealthy, restart the ACI group so it remounts the persistent share, then recover:

```bash
az container restart -g rg-smartdocs-prod -n aci-qdrant-prod
```

### 4. Verify collection counts

Confirm the restored point count matches the expected pre-incident count (capture it from the last good snapshot's metadata or a prior monitoring sample).

```bash
curl -s "http://<qdrant-host>:6333/collections/smartdocs" \
  | sed -n 's/.*"points_count":\([0-9]*\).*/\1/p'

# Smoke a known query end-to-end and confirm a citation comes back
curl -s -X POST https://app-smartdocs-prod.azurewebsites.net/api/ask \
  -H "Content-Type: application/json" \
  -d '{"question":"<known-good question with a known citation>"}'
```

### 5. Resume ingest

Re-enable the ingest worker and clear maintenance mode. Re-run ingest for any documents added after the snapshot timestamp to close the RPO gap.

```bash
az containerapp update -g rg-smartdocs-prod -n ca-ingest-prod \
  --min-replicas 0 --max-replicas 5

az appconfig feature delete --endpoint <appConfigEndpoint> --auth-mode login \
  --feature retrieval-maintenance --yes
```

## Verification

- Point count matches the expected baseline (within the documents added since the snapshot).
- A known-good query returns the correct citation.
- `qdrant.search` p95 and error rate back to nominal (`p95-latency-high.md`).
- Faithfulness sample recovers on the next cycle (`faithfulness-degraded.md`).

## Rollback

There is no "rollback" of a restore — if the chosen snapshot is itself bad (counts wrong, queries off), repeat the procedure with the **next-older** snapshot from the `qdrant-snapshots` listing. Keep ingest stopped until a snapshot verifies clean.

## Escalation

- No usable snapshot exists (job was failing, container empty): this is a data-loss incident — page the platform lead and the retrieval owner, declare Sev-1, and rebuild from source via full re-ingest (`reindex-cutover.md` backfill path against a fresh collection).
- Restore succeeds but counts stay short: the snapshot predates recent ingest — re-ingest the gap window; if the gap is large, communicate expected recovery time to stakeholders.
- This runbook fires repeatedly: escalate the decision to migrate to the managed `azure-search` backend (see DR posture above).
