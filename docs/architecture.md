# Architecture

> Updated at the end of every phase. **Last updated: end of Phase 8.**

The Contoso SmartDocs companion code is shipped across 18 source projects + 4 test projects + 5 chapter samples + tooling + infra. This document is the canonical view; the per-chapter file paths live in [`chapter-map.md`](chapter-map.md).

## High-level component diagram (Phase 7 endpoint)

```mermaid
flowchart TB
    User[User] -->|HTTP / SSE| Api[SmartDocs.Api]

    subgraph Pipeline["Retrieval pipeline"]
      Routing[SmartDocs.Routing<br/>Rule + Semantic + Multi]
      Retrieval[SmartDocs.Retrieval<br/>Dense + Sparse + Hybrid +<br/>Graph + Vectorless +<br/>Decorators &#40;HyDE / Fusion / CRAG&#41;]
      Reranking[SmartDocs.Reranking<br/>Cohere + CrossEncoder]
      Generation[SmartDocs.Generation<br/>RagPipeline + PromptTemplateEngine +<br/>Citations + AuditLogger]
    end

    subgraph Stores
      Qdrant[(Qdrant)]
      Neo4j[(Neo4j)]
      Redis[(Redis)]
    end

    subgraph Cross["Cross-cutting"]
      Core[SmartDocs.Core<br/>IChatClient + IEmbeddingGenerator +<br/>ITokenCounter + LlmClientOptions]
      Ingestion[SmartDocs.Ingestion<br/>Chunking + Embeddings + Multimodal +<br/>Indexing strategies]
      Performance[SmartDocs.Performance<br/>Caches + Polly]
      Security[SmartDocs.Security<br/>Sanitizer + Anomaly + ContentFilter]
      Operations[SmartDocs.Operations<br/>MinHash dedup + Drift + GDPR]
      Evaluation[SmartDocs.Evaluation<br/>Recall@K + Faithfulness]
      Mcp[SmartDocs.Mcp<br/>MCP server tools]
      Agents[SmartDocs.Agents<br/>SmartDocsAgent + Multi-agent workflow]
    end

    Api --> Routing
    Routing --> Retrieval
    Retrieval -->|dense| Qdrant
    Retrieval -->|graph| Neo4j
    Retrieval --> Reranking
    Reranking --> Generation
    Generation --> Api

    Ingestion --> Qdrant
    Ingestion --> Neo4j
    Performance -.cache.-> Redis
    Security -.middleware.-> Api
    Operations -.GDPR / drift.-> Qdrant
    Evaluation -.gates.-> CI[GitHub Actions]
    Agents --> Retrieval
    Mcp --> Retrieval

    Core -.foundation.-> Pipeline
    Core -.foundation.-> Cross
```

## Project dependency summary

| Project | Depends on | Notes |
|---|---|---|
| `SmartDocs.Core` | (none) | Foundation: domain records + interfaces + TokenCounter + LlmClientOptions + AddSmartDocsCore |
| `SmartDocs.Ingestion` | Core | Embeddings + Chunking + Multimodal + Indexing strategies; Roslyn + PdfPig |
| `SmartDocs.Retrieval` | Core | Dense / Sparse (in-process BM25) / Hybrid + Decorators + Graph + Vectorless + VectorStores; Qdrant.Client + Neo4j.Driver |
| `SmartDocs.Reranking` | Core, Retrieval | NoOp + Cohere + CrossEncoder + RerankingMiddleware |
| `SmartDocs.Routing` | Core, Retrieval | MetadataFilter + QueryConstructor + Routers + ConversationalQueryRewriter |
| `SmartDocs.Generation` | Core, Retrieval | PromptTemplateEngine + RagPipeline + Citations + AuditLogger |
| `SmartDocs.Agents` | Core, Retrieval | SmartDocsAgent + MultiAgentWorkflow; MAF Microsoft.Agents.AI + Workflows |
| `SmartDocs.Mcp` | Core, Retrieval | ModelContextProtocol tools |
| `SmartDocs.Security` | Core | Sanitizer + Anomaly + ContentFilter |
| `SmartDocs.Evaluation` | Core, Generation | Retrieval + Generation evaluators |
| `SmartDocs.Operations` | Core | MinHashDeduplicator + DriftAdapter + GdprDeletionPipeline |
| `SmartDocs.Performance` | Core, Generation | EmbeddingCache + QueryCache + PollyPolicies |
| `SmartDocs.Api` | Core, Generation, Retrieval | ASP.NET Core 10 Minimal API; `/health` + `/api/ask` + `/api/ask/stream` (TypedResults.ServerSentEvents) |
| `SmartDocs.Dashboard` | Core, Evaluation (Phase 8 polish) | Blazor Server eval dashboard — placeholder until Phase 8 |

## Update history

- **2026-05-02 (Phase 0)** — first cut. 18 empty projects + Docker Compose stack + dataset generator.
- **2026-05-02 (Phase 1)** — `SmartDocs.Core` foundations + Hello-World sample + SK→MAF migration sample.
- **2026-05-02 (Phase 2)** — full core RAG pipeline (Ch 3–10).
- **2026-05-02 (Phase 3)** — query intelligence (Ch 11–12).
- **2026-05-02 (Phase 4)** — graph + hybrid storage (Ch 13–14).
- **2026-05-02 (Phase 5)** — design patterns (Ch 15–19).
- **2026-05-02 (Phase 6)** — production concerns (Ch 20–24).
- **2026-05-02 (Phase 7)** — capstone (Ch 25): `AddSmartDocsRagPipeline()` + Bicep + Vertical Slice variant.
- **2026-05-02 (Phase 8)** — polish: per-phase reports + samples README + chapter map + this update.
