# Chapter Map

Maps each of the 25 chapters in the syllabus to the projects, files, tests, and samples that exercise it. Updated at the end of every phase. **Last updated: end of Phase 0.**

| # | Part | Chapter | Phase | Primary code | Tests | Samples |
|---|---|---|---|---|---|---|
| 1 | I | The AI Landscape | 1 | `samples/Ch01_HelloWorldRag/` | unit + smoke | Ch01_HelloWorldRag |
| 2 | I | The .NET Toolkit for RAG Development | 1 | `SmartDocs.Core` (interfaces, `TokenCounter`, `LlmClientOptions`); `SmartDocs.Api` skeleton | `TokenCounter` ≤2% error | Ch02_SkToMafMigration |
| 3 | II | Embeddings — Turning Text into Vectors | 2 | `SmartDocs.Ingestion/Embeddings/` (`EmbeddingService`, `BatchEmbeddingPipeline`) | embedding rate-limit / 429 handling | Ch03_EmbeddingBenchmarks |
| 4 | II | Chunking and Contextual Retrieval | 2 | `SmartDocs.Ingestion/Chunking/` (`FixedSizeChunker`, `SentenceChunker`, `RecursiveCharacterChunker`, `SemanticChunker`, `CodeFileChunker`, `ContextualChunker`) | named-entity-not-split assertion | Ch04_ChunkingPlayground, Ch04_ContextualRetrievalDemo |
| 5 | II | Multimodal Content | 2 | `SmartDocs.Ingestion/Multimodal/` (`PdfImageExtractor`, `ImageCaptioner`, `MultimodalChunker`) | retrieval cites Q3 chart | — |
| 6 | II | Vector Databases | 2 | `SmartDocs.Core/VectorStore/` adapters (Qdrant, AzAISearch, InMemory); `IVectorStore` domain port | recall@10 ≥ 0.85 on Contoso | Ch06_VectorDbComparison |
| 7 | II | Indexing Strategies | 2 | `SmartDocs.Ingestion/Indexing/` (4 `IIndexingStrategy` impls + `RaptorIndexer`) | recall comparison test | Ch07_IndexingStrategies |
| 8 | II | The Retriever | 2 | `SmartDocs.Retrieval/` (`DenseRetriever`, `SparseRetriever`, `HybridRetriever`, `RrfMerger`) | golden-set recall ≥ 0.85 | — |
| 9 | II | Re-ranking | 2 | `SmartDocs.Reranking/` (`CohereReranker`, `BgeReranker`, `CrossEncoderReranker`, `RerankingMiddleware`) | A/B harness | — |
| 10 | II | The Complete RAG Pipeline | 2 | `SmartDocs.Generation/RagPipeline.cs`, `PromptTemplateEngine`, `SmartDocs.Api` SSE endpoint | end-to-end p50/p95/p99 latency | — |
| 11 | III | Metadata Filtering and Query Construction | 3 | `SmartDocs.Routing/Filtering/` (`MetadataFilterBuilder`, `QueryConstructor`, filter compilers) | pre/post-filter perf | — |
| 12 | III | Query Routing and Conversational Multi-Turn RAG | 3 | `SmartDocs.Routing/` (`RuleBasedRouter`, `SemanticRouter`, `MultiSourceRouter`, `RoutingOrchestrator`, `ConversationalQueryRewriter`); `AgentSession` integration | routing accuracy ≥ 0.90 | — |
| 13 | IV | Graph Databases for RAG | 4 | `SmartDocs.Retrieval/Graph/` (`Neo4jGraphStore`, `EntityExtractor`, `DocumentToGraphPipeline`, `GraphRetriever`) | Cypher-tool 2-hop org chart | — |
| 14 | IV | Hybrid Databases | 4 | `SmartDocs.Retrieval/Hybrid/` (`HybridDatabaseRetriever`, `FusionService`) | 30–50% recall lift on relationship queries | — |
| 15 | V | Classic Enhancements (HyDE, RAG Fusion, CRAG, Self-RAG, RAPTOR) | 5 | `SmartDocs.Retrieval/Decorators/` (`HydeRetriever`, `RagFusionRetriever`, `CragRetriever`) | per-technique cost tracking | — |
| 16 | V | Vectorless RAG | 5 | `SmartDocs.Retrieval/Vectorless/` (`DocumentStructureParser`, `StructuralIndex`, `VectorlessRetriever`) | structured-query precision | Ch16_VectorlessRetrieval |
| 17 | V | GraphRAG, LazyGraphRAG, Hybrid RAG | 5 | `SmartDocs.Retrieval/Graph/` extension (`GraphRagPipeline`, `LazyGraphRagRetriever`) | indexing-cost reduction order-of-magnitude | Ch17_LazyGraphRagDemo |
| 18 | V | Model Context Protocol | 5 | `SmartDocs.Mcp/` (server + client tools); MAF `HostedMcpTool` integration | tool-poisoning detection | Ch18_McpServer |
| 19 | V | Agentic RAG, Multi-Agent RAG, Agentic Memory | 5 | `SmartDocs.Agents/` (`SmartDocsAgent`, Workflow API graph, MCP tools, Mem0/Letta REST) | 4-agent workflow with loop-back | Ch19_MultiAgentOrchestration |
| 20 | VI | Evaluation and Metrics | 6 | `SmartDocs.Evaluation/`, `SmartDocs.Dashboard/`, `tools/eval-runner`, `tools/deepeval-bridge` | match RAGAS to ±5%; `ci-eval.yml` blocks PRs < 0.85 faithfulness | — |
| 21 | VI | Latency, Cost, Performance, .NET Optimization | 6 | `SmartDocs.Performance/` (`QueryCache`, `EmbeddingCache`, `ResultCache`, `ParallelRetriever`, `SpanEmbeddingPipeline`, Polly v8) | -30% p95 latency; -60% Gen0 GC | — |
| 22 | VI | Freshness, Drift, Model Migration | 6 | `SmartDocs.Operations/` (Service Bus indexer, `MinHashDeduplicator`, `DriftAdapter`, GDPR pipeline) | ≥95% quality recovery at <1% cost on the 6th silo | Ch22_DriftAdapterMigration |
| 23 | VI | Security — Prompt Injection and Adversarial Indexing | 6 | `SmartDocs.Security/` (`InputSanitizer`, `EmbeddingAnomalyDetector`, `ContentFilter`) | 25 OWASP AISVS C08 cases | — |
| 24 | VI | Trust by Design — Grounding, Citations, Compliance | 6 | `SmartDocs.Generation/Citations/`, `FaithfulnessChecker`, `GroundingMetadata` schema, EU AI Act audit logger | hallucinated-specifics detection ≥ 0.90 | — |
| 25 | VII | Production RAG Application — Code to Cloud | 7 | full integration; `infra/azure/*.bicep`; NuGet `services.AddSmartDocsRagPipeline()` | 100-query golden eval as final CI gate | Ch25_VerticalSliceVariant |

## Phase 0 status

- All 14 src/ + 4 tests/ projects exist, contain a `Placeholder.cs` file, and compile cleanly under `<TreatWarningsAsErrors>true>`.
- The `tools/generate-dataset` console produces the 300-document Contoso corpus (with the 6th silo Release Notes & Tickets) deterministically from a fixed seed.
- One xUnit test asserts dataset determinism.

Nothing in this table beyond Phase 0 has been written yet.
