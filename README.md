# Mastering RAG in .NET — Companion Code

Companion code for **_Mastering RAG in .NET: Production Retrieval-Augmented Generation with C#, the Microsoft Agent Framework, and Azure_** by [Rachid Dahir](https://github.com/rachiddahir) (2026 edition).

> The .NET solution, samples, and namespaces stay under the `RAG-in-DotNet` identifier — that is the code path; the title above is the reader-facing brand.

> **Status — All 9 phases ✅ complete (Phases 0–8).** All 25 chapters from the syllabus have been mapped to runnable, tested code: 18 src/ projects + 4 test projects + 5 chapter samples + tooling + infra. **152 tests pass** under warnings-as-errors and format check. See [`docs/chapter-map.md`](docs/chapter-map.md) for the per-chapter file map and [`docs/architecture.md`](docs/architecture.md) for the system view. The Phase 8 polish backlog (deferred chapter samples and stretch features) lives in [`samples/README.md`](samples/README.md) and the per-phase reports under [`docs/phase-reports/`](docs/phase-reports).

---

## What you get

A single evolving project — **Contoso SmartDocs** — built across 25 chapters and 7 parts:

- A 14-project **Clean / DDD-ish** solution (Core, Ingestion, Retrieval, Reranking, Routing, Generation, Agents, Mcp, Security, Evaluation, Operations, Performance, Api, Dashboard) plus a Vertical Slice variant in Ch 25.
- **Six document silos** (HR Policies, Technical Docs, Financial Reports, Legal Contracts, Product Catalog, Release Notes & Support Tickets) with full Ch 11 metadata schema baked in from day 1.
- **Local infra** via Docker Compose: Qdrant + Neo4j + Redis + Ollama (`nomic-embed-text` + `llama3.2` pre-pulled) + Aspire Dashboard.
- **Quality gates**: `dotnet build` warnings-as-errors, `dotnet format --verify-no-changes`, xUnit tests, GitHub Actions CI on every PR.

---

## Versions Covered

| Component | Pinned to | Status (May 2026) |
|---|---|---|
| .NET SDK | 10.0.x (`global.json`) | GA |
| C# language | latest (14) | GA |
| Microsoft Agent Framework (`Microsoft.Agents.AI`) | 1.3.0 | **GA April 3, 2026** |
| `Microsoft.Agents.AI.Foundry` | 1.3.0 | GA |
| `Microsoft.Agents.AI.Workflows` | 1.3.0 | GA — graph-based multi-agent |
| `Microsoft.Agents.AI.A2A` | 1.3.0-preview.* | **Preview** — A2A 1.0 support coming soon |
| `Microsoft.Extensions.AI` | 10.5.1 | GA (10.x is the active line; 9.x covered in errata) |
| `Microsoft.Extensions.VectorData.Abstractions` | 10.5.0 | **Abstractions GA**; most connectors still preview |
| `Microsoft.Extensions.Http.Resilience` / `.Resilience` | 10.5.0 | GA |
| `Microsoft.ML.Tokenizers` | 2.0.0 | GA |
| `OllamaSharp` | 5.4.25 | GA — replaces deprecated `Microsoft.Extensions.AI.Ollama` |
| `Qdrant.Client` | 1.17.0 | GA |
| `Neo4j.Driver` | 6.0.0 | GA — driver SemVer; server is on CalVer 2025.x |
| `Polly` | 8.6.6 | GA |
| `OpenTelemetry` | 1.15.3 | GA |
| `ModelContextProtocol` | 1.2.0 | GA — donated to the Linux Foundation, December 2025 |
| Azure AI Search agentic retrieval | REST `2025-11-01-preview` | **Preview** |
| Semantic Kernel | 1.x | maintenance + selective features through ≥ April 2027 |
| AutoGen | community-maintained, no new features | — |

The truth-source for all NuGet pins is [`Directory.Packages.props`](Directory.Packages.props). Major version drift between this README and the original mission brief is documented in [ADR-0005](docs/decisions/0005-version-drift-from-mission-brief.md).

---

## Quick Start

```bash
# 1. Prerequisites
#    - .NET 10 SDK         https://dot.net
#    - Docker Desktop      https://docker.com
#    - Git                 https://git-scm.com

# 2. Clone & build
git clone https://github.com/rachiddahir/RAG-in-DotNet.git
cd RAG-in-DotNet
dotnet restore
dotnet build

# 3. Spin up local infra (Qdrant, Neo4j, Redis, Ollama, Aspire)
cp infra/.env.example infra/.env
cd infra && docker compose up -d && cd ..

# 4. Generate the 300-document Contoso dataset
dotnet run --project tools/generate-dataset -- --small --output data

# 5. Run all tests
dotnet test

# 6. Run the chapter samples (require Ollama from step 3)
dotnet run --project samples/Ch01_HelloWorldRag      # Ch 1: 80-line Hello-World RAG
dotnet run --project samples/Ch02_SkToMafMigration   # Ch 2: SK→MAF migration walk-through
dotnet run --project src/SmartDocs.Api               # /health: shows the active provider + models
```

---

## Repository Layout

```
RAG-in-DotNet/
├── src/                          14 C# projects evolving across the book
│   ├── SmartDocs.Core            domain models, interfaces, shared abstractions
│   ├── SmartDocs.Ingestion       parsing, chunking, contextual retrieval, embedding, indexing
│   ├── SmartDocs.Retrieval       dense, sparse, hybrid, graph, vectorless retrievers
│   ├── SmartDocs.Reranking       Cohere, BGE, cross-encoder rerankers
│   ├── SmartDocs.Routing         rule-based, semantic, multi-source routing + AgentSession
│   ├── SmartDocs.Generation      prompt templates, context assembly, citation tracking
│   ├── SmartDocs.Agents          AIAgent / ChatClientAgent setups, tools, Workflow API
│   ├── SmartDocs.Mcp             MCP server + client tools
│   ├── SmartDocs.Security        sanitization, anomaly detection, content filtering
│   ├── SmartDocs.Evaluation      RAGAS-style metrics, DeepEval bridge, eval harnesses
│   ├── SmartDocs.Operations      freshness, drift, Drift-Adapter, dedup, GDPR deletion
│   ├── SmartDocs.Performance     caching, Span<T>, Polly, OpenTelemetry, Aspire wiring
│   ├── SmartDocs.Api             ASP.NET Core 10 Minimal API, first-class SSE streaming
│   └── SmartDocs.Dashboard       Blazor Server eval dashboard (Ch 20)
│
├── tests/
│   ├── SmartDocs.UnitTests       xUnit
│   ├── SmartDocs.IntegrationTests Testcontainers-based (Phase 2+)
│   ├── SmartDocs.EvalTests       quality gates with thresholds
│   └── SmartDocs.SecurityTests   25 OWASP AISVS C08 red-team cases (Ch 23)
│
├── samples/                      one folder per chapter that has a micro-project
├── data/                         6 silos + eval-sets/ (populated by tools/generate-dataset)
├── tools/
│   ├── generate-dataset          deterministic synthetic-corpus generator
│   ├── eval-runner               (Phase 6) — JUnit XML emitter for CI gates
│   └── deepeval-bridge           (Phase 6) — Python microservice / subprocess wrapper
├── infra/
│   ├── docker-compose.yml        Qdrant + Neo4j + Redis + Ollama + Aspire
│   └── azure/                    (Phase 7) — Bicep templates
├── .github/workflows/
│   ├── ci.yml                    build + test on PRs
│   └── ci-eval.yml               (Phase 6) — eval gates that block PRs
├── docs/
│   ├── architecture.md           Mermaid diagrams, kept current per phase
│   ├── chapter-map.md            chapter → projects/files/tests/samples
│   ├── decisions/                ADRs (Nygard format)
│   └── phase-reports/            one per phase
├── Directory.Packages.props
├── Directory.Build.props
├── global.json
├── .editorconfig
└── RAG-in-DotNet.slnx            (.NET 10 default — XML format, replaces .sln)
```

---

## Build Plan

The companion code is built in 9 incremental phases. All phases are complete.

| Phase | Chapters | Scope | Status |
|---|---|---|---|
| 0 | — | Repo bootstrap | ✅ |
| 1 | Ch 1–2 | Foundations, Hello-World RAG, SK→MAF migration | ✅ |
| 2 | Ch 3–10 | Core pipeline: embeddings → chunking → multimodal → vector DBs → indexing → retrieval → re-ranking → SSE | ✅ |
| 3 | Ch 11–12 | Query intelligence: metadata filters, query construction, routing, conversational rewriting | ✅ |
| 4 | Ch 13–14 | Graph + hybrid storage: Neo4j adapter, EntityExtractor, FusionService | ✅ |
| 5 | Ch 15–19 | Design patterns: HyDE / RAG-Fusion / CRAG, Vectorless, GraphRAG / LazyGraphRAG, MCP, Multi-agent | ✅ |
| 6 | Ch 20–24 | Production: evaluation, performance, drift / GDPR, security (27-test red team), citations + EU AI Act audit | ✅ |
| 7 | Ch 25 | Capstone: `AddSmartDocsRagPipeline()`, Bicep, Vertical Slice variant | ✅ |
| 8 | — | Polish: per-phase reports, samples README, chapter map, architecture docs | ✅ |

---

## Chapter Map (25 chapters)

The chapter-by-chapter mapping of book content to code lives in [`docs/chapter-map.md`](docs/chapter-map.md) and is updated at the end of every phase.

---

## Working with the dataset

```bash
# Default: --small, seed 42, output ../../data (resolved from the tool's CWD)
dotnet run --project tools/generate-dataset

# Override seed and output location
dotnet run --project tools/generate-dataset -- --seed 123 --output /tmp/my-corpus

# Verify determinism by hand
diff $(dotnet run -q --project tools/generate-dataset -- --output /tmp/a)/manifest.sha256 \
     $(dotnet run -q --project tools/generate-dataset -- --output /tmp/b)/manifest.sha256
```

`--large` (the 5 000-document dataset) is a Phase 8 stretch goal and currently throws.

---

## Contributing

Issues and PRs welcome — please follow the conventional-commit prefixes already in `git log` (`chore:`, `feat:`, `fix:`, `docs:`).

The repo enforces:

- `dotnet build` with `<TreatWarningsAsErrors>true>`
- `dotnet format --verify-no-changes`
- xUnit tests pass on `ubuntu-latest` and `windows-latest`

Run all three locally before opening a PR; CI runs them as gates.

---

## License

MIT. See [`LICENSE`](LICENSE) (added in Phase 7).
