# Runbook: p95 latency high

**Alert:** p95 end-to-end latency > 3s for 10 minutes → page on-call.

This is the user-visible SLO breach. Every second past the budget is a user watching a spinner. Work top-down: find which span owns the time before you touch anything.

## Symptoms

- p95 (and often p99) on the `ask` / `ask-stream` operations climbs above 3s.
- Time-to-first-token grows even when total tokens are unchanged.
- Error rate may still be nominal — this is slowness, not failure (if errors also spike, run `error-rate-high.md` first).

## Diagnostic queries

Find which span owns the latency. The pipeline is `agent → IRetriever → IReranker → LLM`; the dependency telemetry tells you where the time went.

```kql
// App Insights — p95 latency by dependency over the last 30 min
dependencies
| where timestamp > ago(30m)
| where name in ("qdrant.search", "openai.chat", "openai.embeddings", "redis.get", "cosmos.query")
| summarize p95 = percentile(duration, 95), count() by name
| order by p95 desc
```

```kql
// End-to-end request p95 vs the LLM span — is the LLM the bottleneck?
requests
| where timestamp > ago(30m) and name startswith "POST /api/ask"
| summarize req_p95 = percentile(duration, 95) by bin(timestamp, 1m)
| render timechart
```

```kql
// Cache hit ratio — a miss storm pushes every request to the LLM
customMetrics
| where timestamp > ago(30m) and name == "rag.cache.hits" or name == "rag.cache.misses"
| summarize sum(value) by name
```

```bash
# Qdrant container CPU/memory under load
az container show -g rg-smartdocs-prod -n aci-qdrant-prod \
  --query "containers[0].instanceView.currentState" -o json
az monitor metrics list --resource <qdrant-aci-resource-id> \
  --metric CpuUsage --interval PT1M
```

```bash
# Redis CPU + evictions — a hot/undersized cache shows up here
az redis show -g rg-smartdocs-prod -n redis-smartdocs-prod --query sku
az monitor metrics list --resource <redis-resource-id> \
  --metric percentProcessorTime,evictedkeys --interval PT1M
```

## Common causes

- **LLM provider latency.** Azure OpenAI is slow or throttling (check `openai.chat` p95 and any 429s). PTU saturation at peak is the usual culprit.
- **Qdrant CPU under concurrent retrieval.** Vector search is CPU-bound; concurrent top-K over a large collection pins the container and queues requests.
- **Cache miss storm.** A deploy, key-format change, or TTL expiry wave drops the hit rate; every request now pays full retrieval + generation cost.
- **Cold start.** A scaled-to-zero replica or a just-restarted instance pays JIT, connection-pool warm-up, and first-call model latency.

## Mitigation

1. **LLM-bound:** confirm PTU headroom; if saturated, raise the deployment capacity or shift overflow to the PAYG fallback deployment. Verify the resilience pipeline's `AttemptTimeout` is not so high it masks the slowness.
2. **Qdrant-bound:** scale the Qdrant container up (more vCPU) or out to a cluster; lower default `topK` if retrieval quality tolerates it; confirm HNSW `ef` search params aren't set higher than needed.
3. **Cache-miss-bound:** confirm Redis is healthy and reachable; pre-warm the cache for the top queries; verify the cache key still matches (a tenant/filter change can silently invalidate every key).
4. **Cold-start-bound:** raise minimum replica count / set `alwaysOn`; confirm the readiness probe (`/health/ready`) holds traffic off a warming instance.

## Rollback

If the latency regression coincides with the last deploy (compare the alert start time to the deploy timestamp), roll back to the previous good revision:

```bash
# Container Apps: shift 100% traffic back to the previous revision
az containerapp revision list -g rg-smartdocs-prod -n ca-smartdocs-prod -o table
az containerapp ingress traffic set -g rg-smartdocs-prod -n ca-smartdocs-prod \
  --revision-weight <previous-revision>=100 <current-revision>=0

# App Service slot swap back
az webapp deployment slot swap -g rg-smartdocs-prod -n app-smartdocs-prod \
  --slot staging --target-slot production --action swap
```

## Escalation

- If LLM provider latency persists with PTU headroom available, open an Azure OpenAI support case and notify the platform lead.
- If scaling Qdrant does not recover p95 within 30 minutes, page the retrieval owner.
- If latency stays over budget for more than 60 minutes, declare an incident (Sev-2) and start the incident-response workflow.
