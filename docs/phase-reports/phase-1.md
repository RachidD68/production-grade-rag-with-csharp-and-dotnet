# Phase 1 — Foundations (Ch 1–2)

**Status**: ✅ complete
**Date**: 2026-05-02
**Commits**: 6 (`5c8412e`..`HEAD`)
**LOC delta**: +1 595 / −79 across 41 files (commit-stat from `git diff 88b705d..HEAD`).
**Test count delta**: +37 (1 → 38). 33 unit + 1 integration are net-new in Phase 1.

## What was built

| Commit | Subject |
|---|---|
| `5c8412e` | feat(core): foundational interfaces and domain models |
| `1b2f8ea` | feat(core): TokenCounter using Microsoft.ML.Tokenizers |
| `bbab084` | feat(core): LlmClientOptions + AddSmartDocsCore + Api wiring |
| `4440183` | feat(samples): Ch01_HelloWorldRag — 10-minute working RAG |
| `988da64` | feat(samples): Ch02_SkToMafMigration — one before/after pair |
| HEAD | docs: Phase 1 architecture, chapter-map, ADRs, phase-1 report |

Code-shape:

- **`SmartDocs.Core/Documents/`** — 5 records: `Document`, `DocumentChunk`, `EmbeddedChunk`, `RetrievalResult`, `DocumentMetadata` (the Ch 11 metadata schema baked into the dataset since Phase 0).
- **`SmartDocs.Core/Abstractions/`** — 5 interfaces: `IDocumentLoader`, `IChunker`, `IEmbeddingService`, `IVectorStore` (domain port; ADR-0007), `IRetriever`.
- **`SmartDocs.Core/Tokens/`** — `ITokenCounter` + `TokenCounter` (default `cl100k_base`; span-based overload).
- **`SmartDocs.Core/Configuration/`** — `LlmClientOptions` + `LlmProvider { Ollama, AzureOpenAI }`. Bound from `SmartDocs:Llm`, validated with data annotations + custom rule (ApiKey required when AzureOpenAI).
- **`SmartDocs.Core/DependencyInjection/`** — `AddSmartDocsCore(IConfiguration)` registers `IChatClient` + `IEmbeddingGenerator<string, Embedding<float>>` + `ITokenCounter`. Provider switch routes to `OllamaApiClient` or `AzureOpenAIClient.Get*Client(deployment).AsI*Client()`.
- **`SmartDocs.Api/Program.cs`** — Phase-0 stub replaced with a real diagnostic `/health` endpoint reporting provider + models + concrete client types, plus a `partial class Program` so integration tests use `WebApplicationFactory<Program>`.
- **`samples/Ch01_HelloWorldRag/`** — runnable 80-LOC console: in-memory `List<(string, ReadOnlyMemory<float>)>` + brute-force cosine + `[Source N]` prompt template + grounded answer.
- **`samples/Ch02_SkToMafMigration/`** — runnable MAF "after" of one canonical SK pattern (Kernel + KernelFunction + ChatHistory + AutoInvokeKernelFunctions → ChatClientAgent + AIFunction + AgentSession). SK "before" lives as a documentation comment block; no SK packages pinned.
- **6 ADRs total** (4 from Phase 0, 2 new in Phase 1).

## Quality bar

| Gate | Result |
|---|---|
| `dotnet build` (warnings-as-errors) | ✅ 0 warnings, 0 errors |
| `dotnet test` | ✅ 38 passed, 0 failed |
| `dotnet format --verify-no-changes` | ✅ clean |
| Public APIs have XML docs | ✅ |
| Sample files have top-of-file chapter comment | ✅ Ch01, Ch02 both name the chapter & section |
| Banned-API grep | ✅ empty |

## What surprised us

1. **Two parallel Microsoft.Extensions.* version lines.** Runtime / hosting / DI / config / logging / options / http / caching ride 10.0.x (matches .NET 10.0.x runtime); AI / VectorData / Resilience / Resilience.Http ride 10.5.x. My initial `s/10.5.0/10.0.7/g` over-corrected and I had to restore the AI/VectorData/Resilience pins to 10.5.x manually. ADR-0005 already covered the version drift; the dual-cadence note has been added to the README's "Versions Covered" box.
2. **`Microsoft.ML.Tokenizers` 2.0 split encoding data into separate packages.** Without `Microsoft.ML.Tokenizers.Data.Cl100kBase` the runtime throws "data file ... could not be loaded" at construction. `Data.O200kBase` is the same story for the GPT-4o family. Both pinned.
3. **`Microsoft.Bcl.Memory` 9.0.4 (transitive of `Microsoft.ML.Tokenizers` 2.0.0) carries GHSA-73j8-2gch-69rq.** Pinned `Microsoft.Bcl.Memory` 10.0.7 directly to force the patched version through CPM transitive pinning.
4. **MAF 1.3 API names** — `ChatClientAgentOptions` does not have an `Instructions` property; the parameterised `ChatClientAgent` constructor takes `name`, `description`, `instructions`, `tools` positionally. Sessions are created via `agent.CreateSessionAsync()` (the brief's "GetNewThread" is a different abstraction). The migration sample uses the constructor form because it's clearer in the chapter narrative.
5. **`AIFunctionFactory.Create` has overloads that confuse `<see cref="…">`** — switched to `<c>…</c>` to avoid the ambiguity error.
6. **CA1859: change return type for performance** got triggered by a `private static IServiceProvider BuildProvider(…)` test helper; using `ServiceProvider` (the concrete class) silences it cleanly.
7. **Real Ollama integration tests came back the same day** — the Docker Desktop daemon was unreachable when Phase 1 first landed, so unit tests used stubs only. After Docker Desktop became available later in the day, `tests/SmartDocs.IntegrationTests/Ollama/OllamaProviderTests.cs` was added and verified end-to-end (see *Real-Ollama verification* below). Azure OpenAI is still untested against a live deployment because no subscription is available; the wiring is exercised in `LlmClientOptionsTests` (DI construction succeeds with a stubbed key).

## Real-Ollama verification (2026-05-02, post-Docker-install)

`tests/SmartDocs.IntegrationTests/Ollama/OllamaProviderTests.cs` covers the
two Ollama-backed paths through `AddSmartDocsCore` end-to-end. Tests are
gated behind the `RUN_OLLAMA_INTEGRATION=1` environment variable so CI
machines without Ollama don't fail (Phase 6 / Ch 21 will replace the gate
with a Testcontainers.Ollama fixture).

  - `Ollama_provider_can_actually_embed_text` — calls
    `IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync(...)`
    against the live `nomic-embed-text` model and asserts a 768-dim vector
    comes back. ✅ Passed.
  - `Ollama_provider_can_actually_chat` — calls `IChatClient.GetResponseAsync(...)`
    against the live `llama3.2` model and asserts non-empty text. ✅ Passed.

Both Phase 1 samples were also smoke-tested live:

  - `dotnet run --project samples/Ch01_HelloWorldRag` → produced
    *"20. According to Source 1, employees at the Montreal office receive
    20 paid vacation days per fiscal year."* — the cosine retriever picked
    the right chunk and `llama3.2` cited it.
  - `dotnet run --project samples/Ch02_SkToMafMigration` → produced
    *"The current weather in Paris is sunny with a temperature of 22°C."* —
    the MAF `ChatClientAgent` invoked the `get_weather` `AIFunction`
    against real `llama3.2` and rendered a grounded answer. **First
    end-to-end demonstration of MAF tool-calling against a local LLM.**

Side fix: both sample `Program.cs` files now pin `ContentRootPath =
AppContext.BaseDirectory` in `HostApplicationBuilderSettings` so the
`appsettings.json` (copied to `bin/Debug/net10.0/`) resolves regardless of
the caller's CWD. Without this, `dotnet run --project samples/...` from
the repo root threw `FileNotFoundException`.

Test count after verification: **40 passed** (37 unit + 3 integration —
1 health endpoint + 2 Ollama-backed).

## What was deferred

- Real Azure OpenAI integration tests (no subscription available).
- Concrete implementations of `IDocumentLoader`, `IChunker`, `IEmbeddingService`, `IVectorStore`, `IRetriever` — interfaces only; Ch 3–8 implement them in Phase 2.
- `RagPipeline` end-to-end orchestrator + SSE — Phase 2 / Ch 10.
- OpenTelemetry exporter wiring — Phase 6 / Ch 21.
- All 12 src/ projects other than `SmartDocs.Core` and `SmartDocs.Api` still hold a `Placeholder.cs`.

## Next phase

**Phase 2 — Core RAG Pipeline (Ch 3–10)**, gated on user approval per mission §6. Phase 2 is the largest phase of the build (8 sub-phases, one per chapter): embeddings → chunking + Contextual Retrieval → multimodal → vector DBs → indexing strategies → dense/sparse/hybrid retrieval → re-ranking → complete pipeline with first-class SSE.
