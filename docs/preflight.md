# Pre-flight checklist

The list is the production-readiness contract; 12-of-13 is not ready.

Before declaring the system production-ready, every item below must pass. Each is a gate, not a guideline — ship with the gap filled, not around it.

- [ ] **Eval baseline locked** — `eval-baseline.json` committed; CI gate fires on regression.
- [ ] **Red-team suite green** — all 25 cases pass on the deployed environment.
- [ ] **Faithfulness sampler running** — production sampler emitting daily metrics for 7 days.
- [ ] **Audit log writing** — verified by `audit-trace` on a fresh production query.
- [ ] **Erasure path verified** — test DSAR run end-to-end; receipt produced; signature verified.
- [ ] **Tenant guard fires** — synthetic cross-tenant probe blocked and logged.
- [ ] **Secrets in Key Vault** — no environment-variable secrets; gitleaks CI clean.
- [ ] **Smoke test green** — post-deploy smoke covers all SSE events (`sources` / `token` / `done`).
- [ ] **Cost dashboard configured** — per-tenant breakdown visible; FinOps signed off.
- [ ] **Compliance dashboard live** — model card current; consent registry populated; incident register accessible.
- [ ] **Runbooks complete** — one per alert; on-call drilled on each.
- [ ] **Rollback rehearsed** — at least one practice rollback completed.
- [ ] **MAF version pinned to the exact patch** — `Directory.Packages.props` names `1.10.0` explicitly, no floating ranges. The eval gate (Chapter 20) covers behavioural regressions on bump; explicit pins prevent accidental drift between dev, CI, and prod.
