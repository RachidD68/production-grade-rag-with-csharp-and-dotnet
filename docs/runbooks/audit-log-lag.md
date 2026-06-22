# Runbook: audit-log lag

**Alert:** audit-log lag > 5 minutes → page (compliance-affecting).

This pages because it is compliance-affecting. The audit trail is a regulatory obligation, not telemetry: every query that is answered but not yet recorded is an unproven event. Lag means the writer is falling behind the system, and the gap is growing until you act.

## Symptoms

- The age of the newest committed audit record exceeds 5 minutes behind live traffic.
- The audit queue (Service Bus) depth is climbing rather than draining.
- `audit-trace` on a fresh query does not find the record within the expected window.

## Diagnostic queries

```kql
// How far behind is the writer? Newest audit record vs now.
customMetrics
| where timestamp > ago(1h) and name == "audit.writer.lag.seconds"
| summarize max(value) by bin(timestamp, 1m)
| render timechart
```

```kql
// Is the writer throwing, or just slow?
exceptions
| where timestamp > ago(30m) and operation_Name contains "Audit"
| summarize count() by type, outerMessage
| order by count_ desc
```

```bash
# Service Bus queue depth — is the audit queue backing up?
az servicebus queue show -g rg-smartdocs-prod \
  --namespace-name sb-smartdocs-prod -n audit-events \
  --query "{active:countDetails.activeMessageCount, dead:countDetails.deadLetterMessageCount, scheduled:countDetails.scheduledMessageCount}"
```

```bash
# Cosmos throughput — is the audit container throttling (429s / RU saturation)?
az monitor metrics list --resource <cosmos-resource-id> \
  --metric TotalRequestUnits,TotalRequests --interval PT1M \
  --filter "DatabaseName eq 'smartdocs' and CollectionName eq 'audit'"

az cosmosdb sql container throughput show \
  -g rg-smartdocs-prod -a cosmos-smartdocs-prod \
  -d smartdocs -n audit --query "resource.throughput"
```

## Common causes

- **Cosmos throughput exhausted.** The audit container is hitting its RU ceiling and Cosmos is returning 429s; the writer retries and falls behind. This is the most common cause under a traffic spike.
- **Service Bus queue backing up.** The writer consumer is down, scaled to zero, or processing slower than the enqueue rate, so the queue depth grows.
- **Writer fault.** The audit writer is throwing (a bad record, a serialization error, a Key Vault/connection-string issue) and dead-lettering instead of committing.
- **Traffic spike.** A legitimate surge outran the provisioned audit throughput — the system is healthy, the audit path is simply under-provisioned for this load.

## Mitigation

1. **Cosmos RU-bound:** raise the audit container throughput (or enable/raise autoscale max RU) to drain the backlog, then leave headroom above peak.
   ```bash
   az cosmosdb sql container throughput update \
     -g rg-smartdocs-prod -a cosmos-smartdocs-prod \
     -d smartdocs -n audit --max-throughput 40000
   ```
2. **Queue backing up:** confirm the writer consumer is running and scaled; restart or scale out the audit worker; clear any processing stall.
3. **Writer faulting:** inspect dead-lettered messages, fix the root cause, then replay them so no event is lost.
4. **Catch-up (manual):** once throughput/consumer is restored, let the backlog drain and verify the lag metric returns under 5 minutes. If records were dead-lettered, run the catch-up replay so the trail is complete and contiguous — a gap in the audit log is the failure this runbook exists to prevent.

## Rollback

There is no "roll back" for a missed audit record — the trail must be made whole, not abandoned. If a recent deploy introduced the writer fault, roll the app back and *then* replay the dead-lettered/queued events so the audit log is contiguous:

```bash
az containerapp ingress traffic set -g rg-smartdocs-prod -n ca-smartdocs-prod \
  --revision-weight <previous-revision>=100 <current-revision>=0
```

Confirm the lag metric is back under 5 minutes and that `audit-trace` on a fresh query lands within the window before standing down.

## Escalation

- Because this is compliance-affecting, notify the compliance owner as soon as the lag is confirmed, even while mitigating.
- If any audit records were lost (not merely delayed), escalate to compliance and security immediately — a lost record is a reportable gap, not an ops blip.
- If the backlog is not draining within 30 minutes of raising throughput, declare an incident and engage the data-platform owner.
