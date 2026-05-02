# Architecture

> Updated at the end of every phase. **Last updated: end of Phase 0.**

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

Update history:

- **2026-05-02 (end of Phase 0)** — first cut. Projects exist but contain only placeholders.
