# Phase 3 — Query Intelligence (Ch 11–12)

**Status**: ✅ complete
**Test count delta**: +13 (74 → 87 unit)

## What was built

| Ch | Code |
|---|---|
| 11 | `SmartDocs.Routing/Filtering/`: `MetadataFilter` (`Where(predicate)`, `And`, `All`), `QdrantFilterCompiler`, `ExtractedFilter`, `QueryConstructor` (LLM-driven NL → structured filter; markdown-fence tolerant; non-JSON fallback) |
| 12 | `SmartDocs.Routing/`: `IQueryRouter` + `RoutingDecision`, `RuleBasedRouter` (per-silo keyword tables), `SemanticRouter` (LLM-classified silos), `MultiSourceRouter` (rule-first, semantic-fallback), `ConversationalQueryRewriter` (resolves follow-ups against chat history) |

## Conscious simplifications

- `AgentSession` integration noted in the chapter map but not exercised end-to-end here. The Agent (Ch 19) already uses `AgentSession.CreateSessionAsync` so the abstraction is wired; multi-turn AgentSession+rewriter composition lives in the Phase 7 capstone configuration.
- OpenTelemetry tracing on routing decisions deferred to Phase 6 (Ch 21) where the observability stack is set up.
