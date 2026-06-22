# Runbook: reindex cutover (embedding-model migration)

**Alert:** none — this is a planned, operator-driven procedure, not a paged event. Run it when migrating to a new embedding model (or chunking/index-schema change) that requires re-embedding the whole corpus.

A new embedding model means every stored vector is in a different space. You cannot mix old and new vectors in one collection — cosine distance across model boundaries is meaningless. The job is to build a parallel index, prove it is at least as good as the live one, and cut over atomically, with the old index kept one cycle as the rollback. Zero downtime, no quality regression.

## Pre-flight

- Confirm the new model is pinned to an explicit version in the Azure OpenAI deployment (no floating alias) — see `faithfulness-degraded.md` for why.
- Confirm the eval baseline (Chapter 20, `eval/baseline.json`) is current for the live index — it is the gate the shadow index must pass.
- Capture the live collection's point count so you can verify the backfill is complete.

```bash
# Live collection point count (qdrant backend)
curl -s "http://<qdrant-host>:6333/collections/smartdocs" \
  | sed -n 's/.*"points_count":\([0-9]*\).*/\1/p'
```

## Procedure

The flow is: **shadow index -> dual-write -> backfill -> eval-gate -> atomic cutover -> keep old one cycle.**

### 1. Create the shadow index

Provision a second collection (`smartdocs-next`) sized for the new model's vector dimension. The reindex tool owns schema creation.

```bash
dotnet run --project tools/SmartDocs.Reindex -- create-shadow \
  --target smartdocs-next \
  --embedding-model text-embedding-3-large
```

### 2. Turn on dual-write

Flip the dual-write feature flag in Azure App Configuration so every new/updated document is embedded with **both** models and written to both collections. New writes now stay current in the shadow index while you backfill history.

```bash
az appconfig feature set --endpoint <appConfigEndpoint> --auth-mode login \
  --feature reindex-dual-write --yes
```

### 3. Backfill the history

Re-embed the existing corpus into the shadow index. Idempotent and resumable — safe to re-run.

```bash
dotnet run --project tools/SmartDocs.Reindex -- backfill \
  --source smartdocs --target smartdocs-next \
  --embedding-model text-embedding-3-large \
  --batch-size 256 --resume
```

```kql
// App Insights — backfill progress + error rate
customMetrics
| where timestamp > ago(6h) and name in ("reindex.backfilled", "reindex.errors")
| summarize sum(value) by name, bin(timestamp, 5m)
| render timechart
```

Confirm the shadow point count matches live (plus any documents added during backfill).

### 4. Eval-gate the shadow index

This is the cutover gate. Run the offline eval (Chapter 20) against the shadow index and compare to the baseline. Do not proceed unless recall and faithfulness hold within the allowed delta.

```bash
dotnet run --project tools/eval-runner -- --gate \
  --index smartdocs-next \
  --baseline eval/baseline.json \
  --recall-threshold 0.02 --faithfulness-threshold 0.03
```

A non-zero exit means the new model regressed retrieval — stop, investigate (chunking, dimension, normalization), and do **not** cut over.

### 5. Atomic cutover

Point reads at the shadow index by flipping the active-index feature flag. Because retrieval reads the alias from App Configuration at request scope, the switch is atomic and takes effect on the next request — no redeploy, no downtime.

```bash
az appconfig kv set --endpoint <appConfigEndpoint> --auth-mode login \
  --key SmartDocs:Retrieval:ActiveIndex --value smartdocs-next --yes
```

### 6. Keep the old index one cycle (rollback window)

Leave `smartdocs` (old) intact and dual-write **on** for one full backfill/eval cycle. If anything regresses in production, rollback is a single flag flip back.

```bash
# Rollback: point reads back at the old index (instant)
az appconfig kv set --endpoint <appConfigEndpoint> --auth-mode login \
  --key SmartDocs:Retrieval:ActiveIndex --value smartdocs --yes
```

After the rollback window passes clean, retire the old collection and turn dual-write off.

```bash
az appconfig feature delete --endpoint <appConfigEndpoint> --auth-mode login \
  --feature reindex-dual-write --yes
dotnet run --project tools/SmartDocs.Reindex -- drop --target smartdocs
```

## Verification

```kql
// Faithfulness should hold flat across the cutover timestamp — no step down
customMetrics
| where timestamp > ago(2d) and name == "rag.faithfulness.sample"
| summarize avg(value) by bin(timestamp, 1h)
| render timechart
```

- Daily faithfulness sample stays at or above its pre-cutover level.
- p95 retrieval latency unchanged (a larger vector dimension can raise it — watch `qdrant.search`).
- No spike in "no results" / thin-retrieval answers.

## Rollback

The rollback is built into the procedure: the old index is live until you retire it. Flip `SmartDocs:Retrieval:ActiveIndex` back to `smartdocs` (step 6). Because dual-write stayed on, the old index never went stale, so rollback loses no data.

## Escalation

- Shadow index fails the eval gate twice after config fixes: page the retrieval owner; do not cut over.
- Faithfulness or recall regresses in production after cutover despite a passing gate: roll back immediately, then convene the eval/quality team for a baseline review (`faithfulness-degraded.md`).
- Backfill cannot complete (persistent embedding errors / quota): check Azure OpenAI PTU headroom and 429s, then escalate to the platform lead.
