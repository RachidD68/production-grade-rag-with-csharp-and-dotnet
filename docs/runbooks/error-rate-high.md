# Runbook: error rate high

**Alert:** error rate > 2% for 5 minutes → page on-call.

Errors mean requests are failing, not just slowing. Start from the correlation ID surfaced to the user, walk the failing requests back to the dependency that broke, and decide fast whether it is us or an upstream.

## Symptoms

- 5xx rate on `/api/ask` and `/api/ask/stream` rises above 2%.
- Users report failed answers, truncated streams, or a generic error toast carrying a correlation ID.
- The failure may be concentrated (one tenant, one route, one dependency) or broad (everything 5xx).

## Diagnostic queries

A user reports a correlation ID — start there, then widen.

```kql
// Single failing request by the correlation ID the user gave you
requests
| where timestamp > ago(1h)
| where customDimensions["correlationId"] == "<correlation-id>"
| join kind=leftouter (
    exceptions | project operation_Id, type, outerMessage, details
  ) on operation_Id
| project timestamp, name, resultCode, type, outerMessage
```

```kql
// Failure breakdown by route and result code over the last 30 min
requests
| where timestamp > ago(30m) and success == false
| summarize count() by name, resultCode
| order by count_ desc
```

```kql
// Which dependency is throwing — 429 / 401 / timeout?
dependencies
| where timestamp > ago(30m) and success == false
| summarize count() by target, resultCode
| order by count_ desc
```

```kql
// Top exception types from the app
exceptions
| where timestamp > ago(30m)
| summarize count() by type, outerMessage
| order by count_ desc
```

```bash
# Is a dependency simply down?
az redis show -g rg-smartdocs-prod -n redis-smartdocs-prod --query "provisioningState"
az cosmosdb show -g rg-smartdocs-prod -n cosmos-smartdocs-prod --query "provisioningState"
az container show -g rg-smartdocs-prod -n aci-qdrant-prod \
  --query "containers[0].instanceView.currentState.state"
```

## Common causes

- **Upstream 429s (rate limiting).** Azure OpenAI throttling under load; the resilience pipeline's retries are exhausted and the request surfaces a 5xx. Look for `resultCode == 429` on the `openai.*` dependencies.
- **Auth failures.** An expired secret, a rotated key not yet propagated from Key Vault, or a managed-identity/RBAC change. Look for `401`/`403` on a dependency that was healthy minutes ago.
- **Dependency down.** Qdrant container restarted, Redis failed over, Cosmos throttled to the point of `429`/`503`. Readiness should have pulled the instance, so a broad outage means the probe or the dependency itself is the problem.
- **Bad deploy.** A regression shipped in the last revision — error onset lines up with the deploy timestamp.

## Mitigation

1. **429s:** confirm PTU/quota headroom; back pressure abusive tenants via the spend/rate limiter; if provider-wide, shed load and serve the cache-degraded response path.
2. **Auth:** confirm the Key Vault secret/version the app resolves; force a secret refresh or restart to pick up a rotated key; check the app's managed identity still has the role assignment.
3. **Dependency down:** restart or fail over the dependency; confirm `/health/ready` correctly reports it unhealthy so traffic drains; scale the dependency if it is overloaded rather than crashed.
4. **Bad deploy:** roll back (below) — do not debug forward on a paging alert.

## Rollback

If onset matches the last deploy, revert immediately:

```bash
# Container Apps: route traffic back to the last good revision
az containerapp ingress traffic set -g rg-smartdocs-prod -n ca-smartdocs-prod \
  --revision-weight <previous-revision>=100 <current-revision>=0

# App Service: swap the previous slot back into production
az webapp deployment slot swap -g rg-smartdocs-prod -n app-smartdocs-prod \
  --slot staging --target-slot production --action swap
```

Confirm the error rate falls below 2% within 5 minutes of the rollback before standing down.

## Escalation

- Provider-side 429s with no quota headroom: page the platform lead and open an Azure OpenAI support case.
- Auth/RBAC change you cannot reverse: page the security owner.
- Broad outage not recovered within 15 minutes: declare an incident (Sev-1 if all traffic affected) and start the incident-response workflow; post a status update.
