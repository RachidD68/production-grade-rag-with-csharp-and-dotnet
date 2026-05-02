# Phase 4 — Graph + Hybrid Storage (Ch 13–14)

**Status**: ✅ complete
**Test count delta**: +8 (87 → 95 unit)

## What was built

| Ch | Code |
|---|---|
| 13 | `SmartDocs.Retrieval/Graph/`: `GraphEntity` / `GraphRelation` / `EntityExtraction` records; `IGraphStore` port; `Neo4jGraphStore` adapter (`Neo4j.Driver` 6.0; CREATE CONSTRAINT, sanitised label / relation type names, Cypher-parameterised `TraverseAsync`); `EntityExtractor` (LLM strict-JSON, tolerant fallback); `GraphRetriever` (extract query entities → graph traverse → return synthetic chunks); `DocumentToGraphPipeline` (per-chunk extract → name-based dedup → upsert) |
| 14 | `SmartDocs.Retrieval/Hybrid/`: `FusionService` (Rrf / Weighted / Cascade strategies); `HybridDatabaseRetriever` (vector + graph fan-out via `Task.WhenAll`, fused via `FusionService`) |

## Conscious simplifications

- Real-Neo4j integration test deferred — `Neo4jGraphStore` works against the running compose stack but no `RUN_NEO4J_INTEGRATION` test ships in Phase 4. Add in Phase 8 polish if the Ch 13 demos need live verification.
- Phase-4 dedup is lower-cased name match. Phase 5 / Ch 17 (LazyGraphRAG) extends it with token fuzzy-matching.
