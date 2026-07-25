# Runbook: faithfulness degraded

**Alert:** rolling 7-day faithfulness < 0.82 → notify quality team (does not page).

This does not page, but it is the alert that protects the product's reason to exist. A silently-worse model or prompt produces answers that look fine and aren't grounded. Treat it with the same seriousness as an outage — the failure is just slower to surface.

## Symptoms

- The production faithfulness sampler's rolling 7-day mean drops below 0.82.
- No error-rate or latency change — the system is healthy, the *answers* are degrading.
- Spot-checks may show answers that read well but cite the wrong chunk, over-claim, or hallucinate around thin retrieval.

## Diagnostic queries

```kql
// Faithfulness trend — when did it start sliding?
customMetrics
| where timestamp > ago(14d) and name == "rag.faithfulness.sample"
| summarize daily = avg(value) by bin(timestamp, 1d)
| render timechart
```

```kql
// Did the drop coincide with a model/deployment change?
customEvents
| where timestamp > ago(14d) and name in ("deploy.completed", "model.version.changed")
| project timestamp, name, customDimensions
| order by timestamp asc
```

```kql
// Is the degradation concentrated in one tenant, doc type, or query class?
customMetrics
| where timestamp > ago(7d) and name == "rag.faithfulness.sample"
| extend tenant = tostring(customDimensions["tenant"]), docType = tostring(customDimensions["docType"])
| summarize avg(value), count() by tenant, docType
| order by avg_value asc
```

```bash
# Re-run the offline eval gate against the current baseline to quantify the regression
dotnet run --project tools/eval-runner -- --gate \
  --recall-threshold 0.02 --faithfulness-threshold 0.03

# What model version is prod actually resolving right now?
az cognitiveservices account deployment list \
  -g rg-smartdocs-prod -n openai-smartdocs-prod \
  --query "[].{name:name, model:properties.model.name, version:properties.model.version}" -o table
```

## Common causes

- **Silent provider model update.** A model alias (e.g. a non-pinned deployment) rolled to a new build with different grounding behaviour — the most common and most invisible cause.
- **Prompt or augmenter change.** A reranker, prompt-template, or context-window change shipped that subtly worsened grounding without failing any test.
- **Retrieval regression upstream.** Recall dropped (a re-index, a chunking change, a stale shadow index) so the generator is grounding on thinner context.
- **Sampling bias.** The sampler over-represents one document type or tenant whose answers are genuinely harder — a measurement artifact, not a real regression. Rule this out, don't assume it.

## Mitigation

1. **Pin the model version.** Move the Azure OpenAI deployment off any floating alias to an explicit, known-good model version so behaviour stops moving under you.
2. **Check for a silent provider update** in the deployment history; if a roll happened, pin back to the prior version and re-measure.
3. **Tie the decision to the eval baseline (Chapter 20).** Run the eval gate against `eval-baseline.json`; if the candidate (current prod) regresses past the allowed delta, that is your evidence — do not let a worse configuration stay live.
4. **Engage the eval/quality team** to triage whether this is a real regression or sampling bias, and to decide whether to roll back the model/prompt or re-baseline.
5. If a recent prompt/reranker/index change is the cause, revert it and confirm the rolling metric recovers.

## Rollback

```bash
# Pin the Azure OpenAI deployment back to the last known-good model version
az cognitiveservices account deployment create \
  -g rg-smartdocs-prod -n openai-smartdocs-prod \
  --deployment-name gpt-prod \
  --model-name <model> --model-version <last-good-version> \
  --model-format OpenAI --sku-capacity <ptu> --sku-name Standard

# Or revert the app revision that shipped the prompt/reranker change
az containerapp ingress traffic set -g rg-smartdocs-prod -n ca-smartdocs-prod \
  --revision-weight <previous-revision>=100 <current-revision>=0
```

Recovery is gradual: the rolling 7-day window takes days to clear a dip. Confirm the *daily* sample recovers above 0.82 first, then watch the rolling mean climb back.

## Escalation

- Confirmed real regression with no clear cause: page the quality lead and open a quality incident.
- Provider silently changed model behaviour on a pinned version: open an Azure OpenAI support case and notify the platform lead.
- Faithfulness keeps sliding after pinning and reverting: freeze further model/prompt changes and convene the eval team for a full baseline review.
