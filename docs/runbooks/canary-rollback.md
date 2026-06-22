# Runbook: canary rollback (progressive delivery)

**Alert:** canary smoke check fails OR rolling faithfulness drops during a canary -> page the deploy operator (Sev-3, auto-rollback expected).

A canary is live and the green signal turned red. The whole point of progressive delivery is that this is cheap to undo: the previous version is still warm and still serving most of the traffic. Shift traffic back first, diagnose second. Seconds of exposure, not minutes.

There are two surfaces, depending on which component is mid-rollout:

- **MCP server** — Container Apps, weighted revision traffic (`deploy/modules/mcp.bicep`).
- **API** — App Service, blue-green slot swap (`deploy/modules/appService.bicep`).

## Symptoms

- The canary smoke suite (health, a known-good `ask`, citation present) fails against the new revision/slot.
- The rolling faithfulness signal on canary traffic drops below the gate during the 0 -> 10 -> 50 -> 100 ramp.
- Error rate or p95 climbs only on the new revision (check the per-revision split, not the aggregate).

## Diagnostic queries

```kql
// App Insights — is the regression isolated to the new revision/slot?
requests
| where timestamp > ago(30m) and name startswith "POST /api/ask"
| extend rev = tostring(customDimensions["revisionName"])
| summarize p95 = percentile(duration, 95), errors = countif(success == false), count() by rev
| order by errors desc
```

```kql
// Faithfulness on canary traffic only
customMetrics
| where timestamp > ago(1h) and name == "rag.faithfulness.sample"
| extend rev = tostring(customDimensions["revisionName"])
| summarize avg(value), count() by rev
```

## Mitigation — MCP server (Container Apps)

Shift 100% of traffic back to the known-good ("current") revision. This is the same lever the deploy uses to ramp the canary up; you are setting it to 0 on the new revision.

```bash
# List revisions and confirm the previous good one
az containerapp revision list -g rg-smartdocs-prod -n ca-mcp-prod -o table

# Snap all traffic back to the previous revision (canary -> 0%)
az containerapp ingress traffic set -g rg-smartdocs-prod -n ca-mcp-prod \
  --revision-weight <previous-revision>=100 <canary-revision>=0
```

This mirrors `deploy/modules/mcp.bicep`: re-deploying with `canaryWeight=0` and the same `previousRevisionName` achieves the identical end state through IaC. Use the `az` command for the immediate stop, then reconcile the Bicep parameter so the next deploy does not re-ramp.

Deactivate the bad revision once traffic is off it, so it cannot be re-selected:

```bash
az containerapp revision deactivate -g rg-smartdocs-prod -n ca-mcp-prod \
  --revision <canary-revision>
```

## Mitigation — API (App Service slot swap)

If the canary was the staging slot mid-swap, swap back. The previous image is warm in the slot, so the swap-back is fast and atomic.

```bash
# Swap production <- staging back (undo the cutover)
az webapp deployment slot swap -g rg-smartdocs-prod -n app-smartdocs-prod \
  --slot staging --target-slot production --action swap

# If a swap is in progress, complete or cancel it instead of starting a new one
az webapp deployment slot swap -g rg-smartdocs-prod -n app-smartdocs-prod \
  --slot staging --action reset
```

## Verification

- Per-revision/per-slot error rate and p95 return to the pre-canary baseline.
- Faithfulness sample on the now-100% revision is back at or above the gate.
- The bad revision is deactivated (Container Apps) or the slot holds the previous image (App Service).

```bash
# Confirm the live traffic split
az containerapp ingress traffic show -g rg-smartdocs-prod -n ca-mcp-prod -o table
```

## Rollback

This runbook *is* the rollback. There is no further fallback at the traffic layer — once 100% is on the previous good revision/slot, the user-facing regression is resolved. The forward fix (a corrected image) goes through the normal canary again, starting at `canaryWeight=0`.

## Escalation

- Traffic shifted back but the regression persists (it was not the canary): this is a wider incident — start the incident-response workflow and check shared dependencies (LLM provider, Qdrant, Redis) via `p95-latency-high.md` / `error-rate-high.md`.
- Slot swap-back fails or the slot is unhealthy: page the platform lead; consider redeploying the last known-good image tag directly to production.
- Repeated canary failures from the same change: freeze that change, open a quality review, and require an eval-gate pass (`reindex-cutover.md`, Chapter 20 baseline) before the next attempt.
