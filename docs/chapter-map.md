# Chapter Map

Maps each of the 25 chapters in the syllabus to the projects, files, tests, and samples that exercise it. Updated at the end of every phase. **Last updated: end of Phase 8.**

| # | Part | Chapter | Phase | Primary code | Tests / Sample |
|---|---|---|---|---|---|
| 1 | I | The AI Landscape | **1 ✅** | `samples/Ch01_HelloWorldRag/HelloWorldRag.cs` | `HelloWorldRagTests` |
| 2 | I | The .NET Toolkit | **1 ✅** | `SmartDocs.Core/{Documents,Abstractions,Tokens,Configuration,DependencyInjection}/`; `SmartDocs.Api/Program.cs` `/health` | `DomainModelTests`, `TokenCounterTests`, `LlmClientOptionsTests`, `HealthEndpointTests`, `OllamaProviderTests` (gated) |
| 3 | II | Embeddings | **2 ✅** | `SmartDocs.Ingestion/Embeddings/` (`EmbeddingService`, `BatchEmbeddingPipeline`) | `EmbeddingServiceTests`, `BatchEmbeddingPipelineTests`; `samples/Ch03_EmbeddingBenchmarks` |
| 4 | II | Chunking + Contextual Retrieval | **2 ✅** | `SmartDocs.Ingestion/Chunking/` (5 chunkers + Roslyn + `ContextualChunker`) | `ChunkerTests`, `ContextualChunkerTests`; `samples/Ch04_ChunkingPlayground` |
| 5 | II | Multimodal | **2 ✅** | `SmartDocs.Ingestion/Multimodal/` (PdfPig extractor + image captioner + chunker) | `MultimodalChunkerTests` |
| 6 | II | Vector DBs | **2 ✅** | `SmartDocs.Retrieval/VectorStores/` (InMemory + Qdrant; AzAISearch deferred) | `InMemoryVectorStoreTests`, `QdrantVectorStoreTests` (gated) |
| 7 | II | Indexing strategies | **2 ✅** | `SmartDocs.Ingestion/Indexing/` (4 strategies + `IndexingPipelineBuilder`) | `IndexingStrategyTests` |
| 8 | II | Retriever | **2 ✅** | `SmartDocs.Retrieval/` (`Dense`, `Sparse` (in-process BM25; ADR-0009), `Hybrid`, `RrfMerger`) | `RetrieverTests` |
| 9 | II | Re-ranking | **2 ✅** | `SmartDocs.Reranking/` (`NoOp`, `Cohere`, `CrossEncoder`, `RerankingMiddleware`) | `RerankerTests` |
| 10 | II | Complete pipeline + SSE | **2 ✅** | `SmartDocs.Generation/` (`PromptTemplateEngine`, `RagPipeline`); `SmartDocs.Api` `/api/ask` + `/api/ask/stream` (TypedResults.ServerSentEvents) | `RagPipelineTests` |
| 11 | III | Metadata filtering + query construction | **3 ✅** | `SmartDocs.Routing/Filtering/` (`MetadataFilter`, `QdrantFilterCompiler`, `QueryConstructor`) | `MetadataFilterTests`, `QueryConstructorTests` |
| 12 | III | Query routing + AgentSession | **3 ✅** | `SmartDocs.Routing/` (`Rule`, `Semantic`, `MultiSource` routers; `ConversationalQueryRewriter`) | `RouterTests` |
| 13 | IV | Graph DB | **4 ✅** | `SmartDocs.Retrieval/Graph/` (`Neo4jGraphStore`, `EntityExtractor`, `GraphRetriever`, `DocumentToGraphPipeline`) | `GraphTests` |
| 14 | IV | Hybrid + fusion | **4 ✅** | `SmartDocs.Retrieval/Hybrid/` (`FusionService` Rrf/Weighted/Cascade, `HybridDatabaseRetriever`) | `HybridFusionTests` |
| 15 | V | Classic enhancements (HyDE / RAG-Fusion / CRAG) | **5 ✅** | `SmartDocs.Retrieval/Decorators/` | `DecoratorTests` |
| 16 | V | Vectorless RAG | **5 ✅** | `SmartDocs.Retrieval/Vectorless/` (`StructuralIndex`, `DocumentStructureParser`, `VectorlessRetriever`, `StructuralVectorRetriever`) | `VectorlessTests` |
| 17 | V | GraphRAG / LazyGraphRAG | **5 ✅** | `SmartDocs.Retrieval/Graph/` (`GraphRagPipeline`, `LazyGraphRagRetriever`) | `GraphRagTests` |
| 18 | V | MCP server / client | **5 ✅** | `SmartDocs.Mcp/SmartDocsMcpTools` (`Search` / `GetChunk` / `Ingest` `[McpServerTool]`s) | `McpToolsTests` |
| 19 | V | Multi-agent | **5 ✅** | `SmartDocs.Agents/` (`SmartDocsAgent.Create`, `MultiAgentWorkflow`) | `AgentsTests` |
| 20 | VI | Evaluation | **6 ✅** | `SmartDocs.Evaluation/` (`RetrievalEvaluator`, `GenerationEvaluator`) | `EvaluationTests` |
| 21 | VI | Performance / cost | **6 ✅** | `SmartDocs.Performance/` (`EmbeddingCache`, `QueryCache`, `PollyPolicies`) | `PerformanceTests` |
| 22 | VI | Drift / operations | **6 ✅** | `SmartDocs.Operations/` (`MinHashDeduplicator`, `DriftAdapter`, `GdprDeletionPipeline`) | `OperationsTests` |
| 23 | VI | Security | **6 ✅** | `SmartDocs.Security/` (`InputSanitizer`, `EmbeddingAnomalyDetector`, `ContentFilter`) | `SmartDocs.SecurityTests/RedTeamSuite` (27 tests) |
| 24 | VI | Citations + EU AI Act | **6 ✅** | `SmartDocs.Generation/Citations/` (`CitationPipeline`, `AuditLogger`) | `CitationTests` |
| 25 | VII | Capstone | **7 ✅** | `SmartDocs.Core/DependencyInjection/RagPipelineRegistration.cs`; `infra/azure/*.bicep`; `samples/Ch25_VerticalSliceVariant` | — |

## Numbers

- **152 tests pass** (121 unit + 27 security + 4 integration; +2 real-Ollama and +1 real-Qdrant when the corresponding `RUN_*_INTEGRATION=1` env var is set)
- **18 src/ projects + 4 test projects + 5 sample projects** all building under `<TreatWarningsAsErrors>true>`
- **9 ADRs** (Phase-0 through Phase-2 reconciliation decisions)
- **8 phase reports** documenting per-phase scope, simplifications, and deferred items

## What's deferred (Phase 8 polish backlog — see `samples/README.md` and per-phase reports)

- 7 chapter-specific samples (Ch04_ContextualRetrievalDemo, Ch06_VectorDbComparison, Ch07_IndexingStrategies, Ch16_VectorlessRetrieval, Ch17_LazyGraphRagDemo, Ch18_McpServer host, Ch19_MultiAgentOrchestration WorkflowBuilder demo, Ch22_DriftAdapterMigration)
- `SmartDocs.Dashboard` Blazor real-time eval UI (Ch 20)
- `tools/eval-runner` + `tools/deepeval-bridge` (Ch 20)
- AzureAISearchVectorStore adapter (Ch 6)
- `SpanEmbeddingPipeline` zero-allocation hot-path (Ch 21)
- Real Procrustes via MathNet.Numerics in `DriftAdapter` (Ch 22)
- A2A handoff demo (Ch 18 / 19; A2A package is preview-only)
- Mem0 / Letta agentic memory client (Ch 19)
- `--large` 5,000-document dataset mode in `tools/generate-dataset` (always a Phase 8 item)
- Per-flag conditional service registrations in `RagPipelineRegistration` (Ch 25)
- `ci-eval.yml` wired to `tools/eval-runner` against the golden eval set (Ch 20)
