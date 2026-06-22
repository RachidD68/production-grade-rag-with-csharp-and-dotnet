# Code verification log

Captured quality-bar runs for the companion code. Each chapter section records
the exact commands run and a summary of their output.

## Chapter 23

Security chapter companion code: injection detection, ingest sanitization,
tenant guard, output redaction (Luhn-validated), rate limiting, hash-chained
audit log, security levels, plus the keyless-Azure and human-in-the-loop
approval samples.

### Commands and results

```
# 1. Build (warnings-as-errors)
dotnet build RAG-in-DotNet.slnx
#   => Build succeeded. 0 Warning(s) / 0 Error(s).

# 2. Tests
dotnet test RAG-in-DotNet.slnx --no-build
#   => SmartDocs.SecurityTests : Passed 67, Skipped 0, Total 67
#      (PromptShieldDetector now has 6 REAL offline tests via a stub
#       HttpMessageHandler — no skipped live fact).
#   => SmartDocs.UnitTests     : Passed 309, Skipped 1 (ONNX model), Total 310.
#   => SmartDocs.IntegrationTests : Passed 6, Total 6.
#   => SmartDocs.EvalTests has no executable tests (pre-existing; no discoverer).
#   All executed tests green.

# 3. Format
dotnet format RAG-in-DotNet.slnx --verify-no-changes
#   => exit 0 (no formatting changes required).

# 4. Banned-API grep (must be empty)
grep -rEn "ChatAgent\b|IVectorStoreRecordCollection|CreateCollectionIfNotExistsAsync|VectorStoreRecord(Key|Data|Vector)|Microsoft\.Extensions\.AI\.Ollama" \
    src/ tests/ tools/ samples/ --include=*.cs
#   => no matches (empty).
```

### RedTeamSuite distribution (exactly 25 attack cases across 8 categories)

| # | Category | Defence exercised | Cases |
|---|----------|-------------------|-------|
| 1 | Direct prompt injection | `InputSanitizer` | 5 |
| 2 | Jailbreak | `InputSanitizer` | 5 |
| 3 | System-prompt extraction | `InputSanitizer` | 4 |
| 4 | Indirect / planted-document injection | `HeuristicInjectionDetector` (documents[]) | 3 |
| 5 | Cross-tenant leak | `TenantGuard` | 2 |
| 6 | MCP tool-description injection | `HeuristicInjectionDetector` | 2 |
| 7 | PII in output | `OutputRedactor` | 2 |
| 8 | Resource-exhaustion | `InputSanitizer` length cap + `RateLimiter` | 2 |
| | **Total attack cases** | | **25** |

Two control facts (a normal question passes; an embedding outlier is flagged)
are intentionally outside the 25-attack count. Verified by filtered run:
`dotnet test --filter "FullyQualifiedName~RedTeamSuite.Cat"` => 25 passed.

### External-API reflection notes (assembly is the source of truth)

- **Azure AI Prompt Shields is GA but REST-only in .NET — there is NO
  `ShieldPrompt`/`AnalyzePromptShieldAsync` in any shipped `Azure.AI.ContentSafety`
  NuGet** (only `1.0.0-beta.1` and `1.0.0` exist, both pinned to service
  api-version `2023-10-01`, predating Prompt Shields). The package pin has been
  **removed** from `Directory.Packages.props` and the `<PackageReference>` removed
  from `SmartDocs.Security.csproj`. `PromptShieldDetector` is now a thin typed
  `HttpClient` client over the documented REST contract
  (`POST {endpoint}/contentsafety/text:shieldPrompt?api-version=2024-09-01`, body
  `{ userPrompt, documents }`, response `{ userPromptAnalysis.attackDetected,
  documentsAnalysis[].attackDetected }`), built only on the framework
  (`System.Net.Http` + `System.Text.Json` source-gen) — **no Azure SDK package**.
  It stays credential-agnostic: the caller supplies a configured `HttpClient`
  (base address = Content Safety endpoint, plus a `Bearer`/`Ocp-Apim-Subscription-Key`
  auth header). Score is binary (1.0 on any attack, else 0.0); findings are
  `prompt-shields:user-prompt-attack` and `prompt-shields:document-attack[<i>]`.
  It is **genuinely unit-tested offline** (6 tests in `PromptShieldDetectorTests`
  drive it with a stub `HttpMessageHandler` returning canned JSON — user-prompt
  attack, indexed document attack, clean, plus route/method assertions and a
  non-2xx → `HttpRequestException` case). No `AnalyzeText`/`CategoriesAnalysis`/
  `ContentSafetyClient` reference remains.
- **Prompt Shields managed-identity bearer wiring (`Ch23_SecureAzureRag` sample).**
  Acquire an Entra token via `DefaultAzureCredential`/`ManagedIdentityCredential`
  → `GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), ct)`,
  set `httpClient.BaseAddress` + `Authorization = new AuthenticationHeaderValue("Bearer", token.Token)`,
  then `new PromptShieldDetector(httpClient)`. **API shapes verified by reflection
  over Azure.Core:** `TokenRequestContext(string[] scopes, …)` (first ctor arg is
  `string[]`); `AccessToken.Token` is a `string`;
  `TokenCredential.GetTokenAsync(TokenRequestContext, CancellationToken)` returns
  `ValueTask<AccessToken>` (compiler-confirmed by `await`-ing it into an
  `AccessToken`). The sample is guarded on `AZURE_CONTENT_SAFETY_ENDPOINT`: if
  unset it prints the pattern and continues; the program exits 0 offline.
- **MAF human-in-the-loop approval lives in `Microsoft.Extensions.AI`, not
  `Microsoft.Agents.AI`.** The shipped 1.10.0 types are
  `ApprovalRequiredAIFunction`, `ToolApprovalRequestContent` (with
  `.CreateResponse(bool, string)` and a `ToolCall` that is a
  `FunctionCallContent`), and `ToolApprovalResponseContent` — not the
  `ToolApprovalAgent`/`FunctionApprovalRequest` names in the brief. The sample
  uses the real types and the approval content round-trips through
  `AgentResponse.Messages` / a follow-up `RunAsync`.
- **`Azure.Identity.ManagedIdentityCredential(string)` is obsolete.** Used the
  non-obsolete `ManagedIdentityCredential(ManagedIdentityId)` with
  `ManagedIdentityId.SystemAssigned` / `FromUserAssignedClientId(...)`.
- `SearchClient`, `SearchIndexClient`, and `AzureOpenAIClient` all expose
  `(Uri, TokenCredential[, options])` keyless constructors as expected.

## Chapter 24

Trust-by-design / compliance companion code (Option A): eight runnable
`SmartDocs.Operations.Compliance/*` classes plus an `ErasureReceipt`, composed
from the lower layers — the Ch 23 provenance signer + alert sink (Security), the
Ch 24 audit-entry + citation shapes (Generation), the Ch 22 GDPR deletion
pipeline (Operations), and the Ch 20 faithfulness judge (Evaluation). The
`Ch24_TrustByDesign` sample was rewired to consume the real `src/` types instead
of its old self-contained local definitions.

### Commands and results

```
# 1. Build (warnings-as-errors)
dotnet build RAG-in-DotNet.slnx
#   => Build succeeded. 0 Warning(s) / 0 Error(s).

# 2. Tests
dotnet test RAG-in-DotNet.slnx --no-build
#   => SmartDocs.SecurityTests : Passed 67, Skipped 0, Total 67.
#   => SmartDocs.UnitTests     : Passed 332, Skipped 1 (ONNX model), Total 333
#      (+23 new ComplianceTests over the Ch 23 baseline of 309).
#   => SmartDocs.IntegrationTests : Passed 6, Total 6.
#   => SmartDocs.EvalTests has no executable tests (pre-existing; no discoverer).
#   All executed tests green.

# 3. Format
dotnet format RAG-in-DotNet.slnx --verify-no-changes
#   => exit 0 (no formatting changes required).

# 4. Banned-API grep (must be empty)
grep -rEn "ChatAgent\b|IVectorStoreRecordCollection|CreateCollectionIfNotExistsAsync|VectorStoreRecord(Key|Data|Vector)|Microsoft\.Extensions\.AI\.Ollama" \
    src/ tests/ tools/ samples/ --include=*.cs
#   => no matches (empty).

# 5. Sample runs offline
dotnet run --project samples/Ch24_TrustByDesign
#   => exit 0. Ingests a signed corpus (incl. one user's content), logs two
#      grounded queries, erases the user via the Ch 22 GdprDeletionPipeline,
#      prints + verifies the signed ErasureReceipt (camelCase chapter JSON
#      shape, signature verifies True), traces a historical answer with
#      CitationAuditor (provenance "signature valid: True"), and renders + signs
#      the live model card.
```

### Reconciliation notes (book snippet → real repo types)

- **`ChainStep` ids are `string`, not `Guid`.** The chapter printed
  `ChainStep` with `Guid ChunkId`/`Guid DocumentId`; the repo's real
  `DocumentChunk.ChunkId`/`DocumentId` are `string` (e.g. `"hr-001#0"`). Built
  the real `ChainStep`/`AuditTrace` with `string` ids per the locked
  align-book-to-repo direction.
- **No new package.** Model card renders **markdown + a detached HMAC-SHA256
  signature** (lowercase hex via `Convert.ToHexStringLower`, mirroring the Ch 23
  `HmacProvenanceSigner`); no PDF writer was added. `ErasureReceipt` signs the
  canonical JSON with the signature field blanked, so `Verify` is deterministic.
- **Audit read seam added.** The Ch 24 `AuditLogger` (Generation) is write-only
  (`Func<AuditEntry, Task>` sink); auditing a past query needs the inverse, so a
  small `IAuditRecordStore` read port + in-memory impl was added. Production
  binds it to the Cosmos container the logger's sink writes to.
- **Ports reused:** chunk lookup → `IChunkLookup.GetByIdAsync` (Ch 18) with the
  Mcp `InMemoryChunkLookup`; document metadata → new `IDocumentMetadataStore`
  (chunk metadata is denormalised, so this is a fallback seam); audit source →
  `AuditEntry`/`Citation` (Generation.Citations) via `IAuditRecordStore`;
  signer → `IProvenanceSigner`/`HmacProvenanceSigner` (Ch 23 Security); eval
  baseline → `GenerationEvaluator.AssessAsync` (Ch 20 Evaluation), wrapped behind
  an `IFaithfulnessJudge` seam so the sampler is deterministic offline.
- **No dependency cycle.** Added ProjectReferences `Operations → {Security,
  Generation, Evaluation}` (Generation transitively pulls Retrieval). All three
  reference only Core (Generation also Retrieval); none reference Operations, so
  Operations stays a leaf consumer — verified by grepping their csprojs for
  `SmartDocs.Operations` (empty).
- **Analyzer accommodations:** `HumanReviewQueue` keeps its chapter-mandated name
  via a targeted `CA1711` `SuppressMessage` (it genuinely is a queue);
  `ExplanationPanel` takes an injected `IRiskClassifier` (satisfies "gated on an
  injected risk classification" and resolves `CA1822`).

## Chapter 25 — Phase 2 (code)

Production capstone companion code: a real, packable NuGet taxonomy (10 headline
packages + 4 support packages), two deploy-time tools (smoke test + load test), a
tagged-release publish workflow, and a real `Category=RedTeam` test selector.

### Commands and results

```
# 1. Build (warnings-as-errors)
dotnet build -c Release RAG-in-DotNet.slnx
#   => Build succeeded. 0 Warning(s) / 0 Error(s).

# 2. Tests
dotnet test -c Release RAG-in-DotNet.slnx --no-build
#   => All green. UnitTests 332 passed / 1 skipped, SecurityTests 67 passed,
#      IntegrationTests 6 passed (= 405 passing, 1 skipped). EvalTests has no
#      executable tests (pre-existing; no discoverer).

# 3. RedTeam selector (must select exactly the 25 attack cases)
dotnet test tests/SmartDocs.SecurityTests/SmartDocs.SecurityTests.csproj \
    -c Release --no-build --filter "Category=RedTeam"
#   => Passed! 25 passed / 0 failed (the 2 control facts are correctly excluded).

# 4. Format
dotnet format RAG-in-DotNet.slnx --verify-no-changes --severity warn
#   => exit 0 (no formatting changes required).

# 5. Banned-API grep (must be empty)
grep -rEn "ChatAgent\b|IVectorStoreRecordCollection|CreateCollectionIfNotExistsAsync|VectorStoreRecord(Key|Data|Vector)|Microsoft\.Extensions\.AI\.Ollama" \
    --include=*.cs --include=*.csproj --include=*.props .
#   => no matches (empty).

# 6. Pack every IsPackable project (no new package added to Directory.Packages.props)
dotnet pack RAG-in-DotNet.slnx -c Release --no-build -o $TEMP/nupkgs
#   => 14 .nupkg + 14 matching .snupkg, all with <readme> + Source Link repo/commit:
#      10 headline  : SmartDocs.Core, .Ingestion, .Reranking.Onnx, .Reranking.Cohere,
#                     .Retrieval.Qdrant, .Retrieval.Postgres, .Retrieval.AzureSearch,
#                     .Mcp, .Evaluation, .Security
#      4 support    : SmartDocs.Retrieval, .Reranking, .Routing, .Generation
#      e.g. SmartDocs.Retrieval.Qdrant depends on SmartDocs.Core 1.0.0 +
#           SmartDocs.Retrieval 1.0.0 (a real dependency edge).

# 7. Tools run (framework-only; no NBomber, no new package)
dotnet run --project tools/SmartDocs.SmokeTest -c Release -- --baseurl <dead-port>
#   => parses --baseurl, runs /health + /api/ask/stream (SseParser) + /admin/metrics,
#      fails gracefully, exits 1 on failed assertions.
dotnet run --project tools/SmartDocs.LoadTest  -c Release -- --baseurl <dead-port> \
    --rps 20 --minutes 0.05 --concurrency 10
#   => open-loop scheduler + Channel + worker pool; reports p50/p95/p99, throughput,
#      error rate; exits 1 when no request succeeds.
```

### Reconciliation notes (taxonomy → real repo)

- **Leaf packages use type-forwarding, not a physical move.** Every adapter named
  for a leaf package (`QdrantVectorStore`, `AzureAiSearchVectorStore` +
  `AzureAiSearchHybridRetriever`, `PostgresHybridRetriever`, `CohereReranker`) is
  referenced by its umbrella's own DI `ServiceCollectionExtensions` (and the Qdrant
  /Azure adapters also depend on umbrella-internal helpers — `QdrantFilterCompiler`,
  `AzureSearchFilterCompiler`, `SearchDocumentRecord`). A physical move would form a
  leaf↔umbrella reference cycle. So each leaf is a **separate packable csproj that
  references the umbrella** and re-exports the adapter via
  `[assembly: TypeForwardedTo(...)]` (the spec's sanctioned fallback). This keeps the
  umbrella DI, the unit/integration tests, and the Ch 6/9/14 samples building with
  **zero edits**, while still producing a real package with a real dependency edge
  (verified in the Qdrant nuspec: `dependency id="SmartDocs.Retrieval"`).
- **Packaging defaults live in `Directory.Build.targets`, not `Directory.Build.props`.**
  The defaults are gated on `'$(IsPackable)' == 'true'`, and a project sets
  `IsPackable` in its body — evaluated AFTER the `.props` import but BEFORE the
  `.targets` import. In `.props` the condition saw an empty `IsPackable` and the whole
  block (README, Source Link, symbols) was silently skipped (the "missing a readme"
  pack warning). Moving it to `.targets` fixed the `<readme>` nuspec element and the
  snupkg emission. Source Link is SDK-native — **no `Microsoft.SourceLink.GitHub` pin**.
- **`IsAotCompatible` deferred on `SmartDocs.Core` (TODO in the csproj), kept on
  `SmartDocs.Mcp`.** Core's `AddSmartDocsCore` binds options from `IConfiguration`
  and runs `ValidateDataAnnotations` (reflection → IL2026/IL3050), and Core pulls
  `Azure.AI.OpenAI` + `OllamaSharp` (not AOT-annotated), so the flag fails the
  warnings-as-errors build; annotating `AddSmartDocsCore` with
  `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` would cascade onto ~6 callers
  (Api, samples, tests). Mcp's own surface is AOT-clean, so it keeps the flag (builds
  0/0). Documented in `src/SmartDocs.Core/SmartDocs.Core.csproj`. The Ch 21 AOT MCP
  sample is self-contained and does not depend on Core, so nothing regresses.
- **Strong-naming deferred (TODO).** Not applied: the `Microsoft.Agents.AI.*` and
  several other pinned transitive dependencies are not strong-named, so signing the
  src libraries would not produce a fully strong-named closure and risks
  `InternalsVisibleTo` public-key friction across the test projects. Left as a
  reasoned TODO rather than break the green build — see below.
- **No new NuGet package.** SmokeTest/LoadTest are framework-only (HttpClient,
  System.Text.Json, `System.Net.ServerSentEvents.SseParser`, Channels, Stopwatch).
  The publish workflow's SBOM step installs the CycloneDX *global tool* on the CI
  runner (a build-time tool, not a `Directory.Packages.props` reference).


## Chapter 25 — Phase 3 (code)

Production-hardening supporting code for the Ch 25 capstone: a configured
resilience profile for the LLM/embedding HTTP clients, liveness/readiness health
checks, a conversation-state persistence seam (in-memory + Redis), a budget
governor that *blocks* (not just alerts) a runaway tenant, a config-backed
feature-gate, and a runnable zero-downtime reindex tool. Package-free — no new
NuGet pin; only the shared framework + already-pinned packages.

### What shipped

- **3.1 Resilience** — `ResilienceWiring.AddResilientLlmHttpClient` (Ch 21, in
  `SmartDocs.Performance`) now configures `AddStandardResilienceHandler` to the
  Ch 25 profile: `Retry.MaxRetryAttempts=4`, `UseJitter=true`,
  `BackoffType=Exponential`, `AttemptTimeout=30s`, `CircuitBreaker.FailureRatio=0.5`,
  `TotalRequestTimeout=100s`. **Judgment call:** the brief's
  `CircuitBreaker.SamplingDuration=30s` violates the framework's validator rule
  (`SamplingDuration ≥ 2 × AttemptTimeout` ⇒ ≥ 60 s); clamped to **60 s** and
  documented in XML + `ResilienceWiring.StandardProfile`. Wired into
  `Program.cs` for the `llm` and `embeddings` clients.
- **3.3 Health checks** — package-free `IHealthCheck`s in
  `SmartDocs.Api/HealthChecks/`: `QdrantHealthCheck`, `OpenAiHealthCheck`,
  `RedisHealthCheck`, `CosmosHealthCheck` (+ shared `HttpProbe`,
  `DependencyEndpointOptions`, `SmartDocsHealthChecks` registration — renamed from
  `HealthCheckRegistration` to avoid a clash with the framework type). Each degrades
  (not throws) offline. `/health/live` (tag `live`) + `/health/ready` (tag `ready`)
  mapped; `/health` kept.
- **3.5 Conversation state** — `IConversationStateStore` (Core) +
  `InMemoryConversationStateStore` (Core) + `DistributedConversationStateStore`
  (Performance, over `IDistributedCache`). TTL = consent/retention boundary
  (Ch 24). DI: `AddInMemoryConversationState` / `AddDistributedConversationState`.
- **3.6 Budget enforcement** — `BudgetEnforcingPipeline : IRagPipeline`
  (`SmartDocs.Operations/Budgeting/`) with `TenantSpendLedger`, `BudgetPolicy`,
  `BudgetDenialReason`, `BudgetExceededException`. Reuses Ch 23 `RateLimiter` + Ch 21
  `TokenPricing`/`CostMeter`. Injected `TimeProvider`. DI `AddBudgetEnforcement`
  decorates the registered `IRagPipeline` (manual decoration — no Scrutor dep added).
  `/api/ask` maps `BudgetExceededException` → 429.
- **3.7 Feature gate** — `IFeatureGate` + `ConfigurationFeatureGate` (Core, reads
  `Features` section, fail-closed). DI `AddFeatureGate` (scoped). Real consumers:
  `/api/ask` reads `graph-retrieval.enabled` per request; budget enforcement gated
  on `Features:budget.enforcement.enabled` at composition.
- **3.2 Reindex tool** — `tools/SmartDocs.Reindex/` (net10.0 console, `IsPackable=false`,
  added to slnx). Six-stage zero-downtime migration (shadow → dual-write → backfill →
  eval-gate → atomic cutover → keep old one cycle) reusing Ch 22 `BackfillScheduler`
  (ParallelShadowThenCutover), `DriftAdapter`, `DocumentReingestService`. Offline
  against `InMemoryVectorStore` + stub embedders; exits 0.

### Commands and results

```
# 1. Build (warnings-as-errors, CA1068/CA2007 etc.)
dotnet build -c Release RAG-in-DotNet.slnx
#   => Build succeeded. 0 Warning(s) / 0 Error(s).

# 2. Tests
dotnet test -c Release RAG-in-DotNet.slnx --no-build
#   => SmartDocs.SecurityTests    : Passed 67,  Total 67.
#   => SmartDocs.IntegrationTests : Passed 15,  Total 15  (+9: health checks/endpoints).
#   => SmartDocs.UnitTests        : Passed 361, Skipped 1 (ONNX model), Total 362
#      (+29: resilience profile, conversation store, budget governor, feature gate).
#   All executed tests green.

# 3. Format
dotnet format --verify-no-changes RAG-in-DotNet.slnx
#   => exit 0.

# 4. Banned-API grep (must be empty)
grep -rEn "ChatAgent\b|IVectorStoreRecordCollection|CreateCollectionIfNotExistsAsync|VectorStoreRecord(Key|Data|Vector)|Microsoft\.Extensions\.AI\.Ollama" \
    src/ tests/ tools/ samples/ --include=*.cs
#   => no matches (empty).

# 5. Reindex tool runs offline, exit 0
dotnet run --project tools/SmartDocs.Reindex -c Release
#   => six stages print, eval gate PASS (live & shadow hit-rate 100%), exit 0.
```

### Judgment calls

- **CircuitBreaker SamplingDuration clamped 30s → 60s** (framework validator
  requires ≥ 2 × AttemptTimeout). All other resilience numbers match the brief.
- **No new package.** Budget DI decoration done by hand (capture + rebind the
  `IRagPipeline` descriptor) rather than adding Scrutor to `SmartDocs.Operations`.
  Cosmos durable store noted in XML only (`Microsoft.Azure.Cosmos` NOT added);
  Cosmos/Redis/OpenAI/Qdrant health probes are plain `HttpClient`/`IDistributedCache`,
  no SDK pins. Feature management noted as Azure App Configuration in XML
  (`Microsoft.FeatureManagement` NOT added).
- **`SmartDocsHealthChecks`** registration class renamed from `HealthCheckRegistration`
  to avoid `CS0104` against `Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckRegistration`.
- **Endpoints rebound to `IRagPipeline`.** `/api/ask` + `/api/ask/stream` previously
  injected the concrete `RagPipeline`, which bypassed the decorator seam; switched to
  `IRagPipeline` so the budget governor (and any cache decorator) actually applies.
