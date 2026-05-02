# Phase 2 — Core Pipeline (Ch 3–10)

**Status**: ✅ complete
**Date**: 2026-05-02
**Commits**: 9 (8 chapter commits + this docs commit)
**Test count delta**: +33 (38 → 71 unit + 1 integration)

## What was built (chapter by chapter)

| Ch | Code |
|---|---|
| 3 | `SmartDocs.Ingestion/Embeddings/`: `EmbeddingService` + `BatchEmbeddingPipeline` (Polly retry on 429); `samples/Ch03_EmbeddingBenchmarks` |
| 4 | `SmartDocs.Ingestion/Chunking/`: `FixedSize`, `Sentence`, `RecursiveCharacter`, `Semantic`, `CodeFile` (Roslyn), `ContextualChunker` (Anthropic late-2024); `samples/Ch04_ChunkingPlayground` |
| 5 | `SmartDocs.Ingestion/Multimodal/`: `MultimodalChunk`, `IPdfImageExtractor`, `PdfPigImageExtractor` (local fallback), `ImageCaptioner`, `MultimodalChunker` |
| 6 | `SmartDocs.Retrieval/VectorStores/`: `InMemoryVectorStore`, `QdrantVectorStore` (real-Qdrant integration test passes); Azure AI Search adapter deferred to Phase 7 |
| 7 | `SmartDocs.Ingestion/Indexing/`: 4 `IIndexingStrategy` impls + `IndexingPipelineBuilder` |
| 8 | `SmartDocs.Retrieval/`: `DenseRetriever`, `SparseRetriever` (in-process BM25 — see ADR-0009), `RrfMerger`, `HybridRetriever` |
| 9 | `SmartDocs.Reranking/`: `IReranker`, `NoOpReranker`, `CohereReranker`, `CrossEncoderReranker` (LLM-as-judge stand-in for BGE), `RerankingMiddleware` |
| 10 | `SmartDocs.Generation/`: `PromptTemplateEngine` (token-budget aware, `[Source N]` markers), `RagPipeline` (one-shot + streaming); `SmartDocs.Api/`: `POST /api/ask` + `POST /api/ask/stream` via ASP.NET Core 10's first-class `TypedResults.ServerSentEvents` |

## Quality bar

| Gate | Result |
|---|---|
| `dotnet build` | ✅ 0 warnings, 0 errors |
| `dotnet test --no-build` | ✅ 72 passed (71 unit + 1 integration smoke); +1 real-Qdrant integration when `RUN_QDRANT_INTEGRATION=1`, +2 real-Ollama integrations when `RUN_OLLAMA_INTEGRATION=1` |
| `dotnet format --verify-no-changes` | ✅ clean |
| Banned-API grep | ✅ empty |

## Conscious simplifications

These shortcuts kept Phase 2 shippable in one work session; each is documented inline in the relevant commit and lifted in a later phase.

1. **`SparseRetriever` is in-process BM25, not Azure AI Search.** ADR-0009. Phase 7 adds the Azure AI Search adapter as the production-default.
2. **`AzureAISearchVectorStore` is not implemented.** The `IVectorStore` adapter pattern is identical; the Phase 7 capstone fills it in.
3. **`SemanticChunker` lives but isn't exercised by a sample** — it needs a live embedding generator and is slow. Ch 4 demonstrates the four mainstream strategies in `samples/Ch04_ChunkingPlayground`.
4. **`ContextualRetrievalDemo` (the second Ch 4 sample) is deferred** — the wrapping logic is in `ContextualChunker`; a full demo against the legal silo is a Phase 8 polish item.
5. **Ch 5 `MultimodalChunker` assigns everything to page 1** — Phase 5 (Ch 17) introduces a page-aware text chunker for the GraphRAG path.
6. **`Microsoft.Extensions.VectorData` adapter pattern not used** — direct provider SDKs (Qdrant.Client) are simpler for the Phase-2 narrative and avoid the generic-key ceremony. The brief's renamed types (`VectorStore`, `VectorStoreCollection<,>`) are still tracked by the banned-API grep so we don't accidentally regress to the pre-rename names.
7. **`samples/Ch10_*` (the Blazor SSE demo)** is deferred — `POST /api/ask/stream` is exercised live via `curl` and the Phase 6 dashboard subsumes the demo UI.

## Real-provider verifications (Docker + Ollama up)

- `dotnet run --project src/SmartDocs.Api` → `/health` returns the bound provider details; `/api/ask` answers HR questions against the seed corpus via real `llama3.2`; `/api/ask/stream` emits `event: sources`, `event: token`, `event: done` SSE frames.
- `RUN_QDRANT_INTEGRATION=1 dotnet test` exercises the Qdrant round-trip against `localhost:6334`.

## What was deferred to later phases

- Azure AI Search adapter (Phase 7 — capstone)
- Production-grade Cohere Rerank A/B harness (Phase 6)
- DeepEval / RAGAS bridge (Phase 6 — Ch 20)
- Token-budget benchmarks (Phase 6 — Ch 21)
- Multimodal page-aware chunker (Phase 5 — Ch 17)

## Next phase

Phase 3 — Query Intelligence (Ch 11–12): metadata filters, query construction, routing, conversational query rewriting, AgentSession integration.
