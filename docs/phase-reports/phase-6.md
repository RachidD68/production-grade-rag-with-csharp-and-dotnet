# Phase 6 — Production Concerns (Ch 20–24)

**Status**: ✅ complete
**Test count delta**: +18 (110 → 121 unit) + **27 net-new security tests** (red-team suite)

## What was built

| Ch | Code |
|---|---|
| 20 | `SmartDocs.Evaluation/`: `RetrievalEvaluator.Evaluate(samples, k)` returns Recall@K / Precision@K / MRR / nDCG@K; `GenerationEvaluator` (LLM-as-judge faithfulness scoring per sentence; aggregates to [0,1]) |
| 21 | `SmartDocs.Performance/`: `EmbeddingCache` (`IEmbeddingGenerator` decorator, SHA-256 keying, partial-batch hits); `QueryCache` (`RagResponse` keyed by normalised query, TTL); `PollyPolicies` (LLM API / Vector DB / Graph DB pre-configured `ResiliencePipeline`s with exponential-backoff retry + circuit breaker + timeout) |
| 22 | `SmartDocs.Operations/`: `MinHashDeduplicator` (64-perm signatures, threshold-driven dedup); `DriftAdapter` (Phase-5 placeholder identity transform; Procrustes via MathNet.Numerics noted for production); `GdprDeletionPipeline` (vector + graph delete + audit-trail emission) |
| 23 | `SmartDocs.Security/`: `InputSanitizer` (direct injection / jailbreak / system-prompt extraction / invisible-char), `EmbeddingAnomalyDetector` (cosine-to-centroid), `ContentFilter` (system-prompt echo / credential shapes); `tests/SmartDocs.SecurityTests/RedTeamSuite.cs` ships 27 OWASP AISVS C08 tests |
| 24 | `SmartDocs.Generation/Citations/`: `Citation` / `GroundedAnswer` records; `CitationPipeline.ExtractCitations` (`[Source N]` markers); `AuditEntry` / `AuditLogger` with injectable sink + `JsonlFileSink` helper |

## Conscious simplifications

- **DeepEval bridge**: `tools/deepeval-bridge/` not yet scaffolded; the C#-side `GenerationEvaluator` ships as the in-process equivalent. Phase 8 polish adds the Python sidecar if the chapter author needs cross-tooling validation.
- **`SmartDocs.Dashboard`** Blazor real-time dashboard from Ch 20 still holds its Phase-0 placeholder. The metrics pipeline is in place; Phase 8 polish adds the Blazor pages when the chapter narrative is finalised.
- **`SpanEmbeddingPipeline`** (Ch 21 zero-allocation hot-path) deferred — the `EmbeddingCache` covers the more common production win (skipping unchanged docs); Span-based optimisation is the spec's "Going Deeper" path.
- **`DriftAdapter`** ships as the algorithm shape only (identity transform after Train). Real Procrustes (SVD via MathNet.Numerics) would be a 30-line swap; left as a Phase 8 stretch.
- **EU AI Act audit log retention policies** (the regulatory configuration around `AuditLogger` sinks) live in the chapter narrative, not in code.
