# Architecture

> Updated at the end of every phase. **Last updated: end of Phase 1.**

This document is the canonical view of the SmartDocs architecture as it stands *today*. As chapters add capabilities, the diagrams below grow.

## Phase 0 — Project Skeleton

At the end of Phase 0 the solution compiles but no real RAG code is written yet. Each box below is an empty C# project containing only a `Placeholder.cs` file naming the chapter that will populate it.

```mermaid
flowchart LR
    subgraph SRC["src/"]
      Core[SmartDocs.Core]
      Ingestion[SmartDocs.Ingestion]
      Retrieval[SmartDocs.Retrieval]
      Reranking[SmartDocs.Reranking]
      Routing[SmartDocs.Routing]
      Generation[SmartDocs.Generation]
      Agents[SmartDocs.Agents]
      Mcp[SmartDocs.Mcp]
      Security[SmartDocs.Security]
      Evaluation[SmartDocs.Evaluation]
      Operations[SmartDocs.Operations]
      Performance[SmartDocs.Performance]
      Api[SmartDocs.Api]
      Dashboard[SmartDocs.Dashboard]
    end

    subgraph TOOLS["tools/"]
      GenerateDataset[generate-dataset]
      EvalRunner["eval-runner (Phase 6)"]
      DeepEvalBridge["deepeval-bridge (Phase 6)"]
    end

    subgraph TESTS["tests/"]
      UnitTests
      IntegrationTests
      EvalTests
      SecurityTests
    end

    subgraph INFRA["infra/ — Docker Compose"]
      Qdrant
      Neo4j
      Redis
      Ollama
      AspireDashboard["Aspire Dashboard"]
    end

    UnitTests --> GenerateDataset
```

Project references in Phase 0 are minimal — only the `SmartDocs.UnitTests → tools/generate-dataset` reference exists. Real dependencies between `src/` projects are added as code lands in Phases 1–7.

## Target Architecture (Phase 7 endpoint — informational)

The full Contoso SmartDocs runtime is a layered RAG pipeline driven by a Microsoft Agent Framework agent. This diagram is **forward-looking** — none of the boxes besides `SmartDocs.Api` and `Dashboard` produce running code yet.

```mermaid
flowchart TB
    User[User] -->|HTTP/SSE| Api[SmartDocs.Api]

    Api --> Agents[SmartDocs.Agents<br/>ChatClientAgent + Workflow API]
    Agents --> Routing
    Agents --> Mcp[SmartDocs.Mcp<br/>tools]

    Routing --> Retrieval
    Retrieval -->|dense| Qdrant[(Qdrant)]
    Retrieval -->|sparse / hybrid| AzSearch[(Azure AI Search)]
    Retrieval -->|graph| Neo4j[(Neo4j)]
    Retrieval --> Reranking
    Reranking --> Generation
    Generation --> Api

    Ingestion --> Qdrant
    Ingestion --> Neo4j

    Performance -.cache.-> Redis[(Redis)]
    Performance -.OTLP.-> AspireDashboard

    Security -.middleware.-> Api
    Evaluation -.gates.-> CI[GitHub Actions]
    Operations -.drift / GDPR.-> Qdrant
```

## Phase 1 — Foundations Land

By the end of Phase 1, the foundation is in place: `SmartDocs.Core` ships
domain models and interfaces, `TokenCounter`, `LlmClientOptions`, and
`AddSmartDocsCore()`. `SmartDocs.Api` exposes `/health`. Two samples
(`Ch01_HelloWorldRag`, `Ch02_SkToMafMigration`) prove the wiring end-to-end.

```mermaid
flowchart LR
    Config["appsettings.json<br/>(SmartDocs:Llm.Provider)"] --> AddCore

    subgraph Core["SmartDocs.Core"]
      Domain["Documents/<br/>Document, DocumentChunk,<br/>EmbeddedChunk, RetrievalResult,<br/>DocumentMetadata"]
      Abstractions["Abstractions/<br/>IDocumentLoader, IChunker,<br/>IEmbeddingService, IVectorStore,<br/>IRetriever"]
      Tokens["Tokens/<br/>ITokenCounter, TokenCounter<br/>(cl100k_base default)"]
      Options["Configuration/<br/>LlmClientOptions<br/>(Ollama | AzureOpenAI)"]
      AddCore["DependencyInjection/<br/>AddSmartDocsCore()"]
    end

    AddCore -->|registers| ChatClient["IChatClient"]
    AddCore -->|registers| EmbedGen["IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;"]
    AddCore -->|registers| TokenCounter

    ChatClient -.Ollama.-> Ollama["OllamaApiClient<br/>http://localhost:11434"]
    ChatClient -.AzureOpenAI.-> AOAI["AzureOpenAIClient<br/>+ AsIChatClient()"]
    EmbedGen -.Ollama.-> Ollama
    EmbedGen -.AzureOpenAI.-> AOAIEmb["AzureOpenAIClient<br/>+ AsIEmbeddingGenerator()"]

    Api["SmartDocs.Api<br/>GET /health"] -->|uses| AddCore
    Ch01["samples/Ch01_HelloWorldRag<br/>brute-force cosine + grounded answer"] -->|uses| AddCore
    Ch02["samples/Ch02_SkToMafMigration<br/>ChatClientAgent + AIFunction"] -->|uses| AddCore
```

The other 13 src/ projects still contain only `Placeholder.cs`.

Update history:

- **2026-05-02 (end of Phase 0)** — first cut. Projects exist but contain only placeholders.
- **2026-05-02 (end of Phase 1)** — `SmartDocs.Core` populated; `SmartDocs.Api/health` live; Ch01 + Ch02 samples runnable; 38 tests green.
