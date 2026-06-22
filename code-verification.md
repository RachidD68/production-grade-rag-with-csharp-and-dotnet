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
