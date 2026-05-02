# Phase 0 — Repo Bootstrap

**Status**: ✅ complete
**Date**: 2026-05-02
**Commits**: 6 (`6804bfb`..`HEAD`)
**LOC delta**: +2 718 hand-authored across `src/`, `tests/`, `tools/`, `infra/`, `.github/`, `docs/`, and root config files. (Total tree counts ~62 K because the Blazor Web App template ships ~57 K of vendored CSS / JS / source maps under `wwwroot/` and `Components/`. None of those are intended to be hand-edited.)
**Test count delta**: +1 (`GenerateDatasetDeterminismTests.Small_dataset_is_deterministic_across_runs`)

## What was built

| Commit | Subject |
|---|---|
| `6804bfb` | chore: scaffold solution and root configuration |
| `e03a385` | chore: scaffold src/ and tests/ projects |
| `d314cb3` | chore: docker-compose stack for Qdrant, Neo4j, Redis, Ollama, Aspire |
| `c2a88ca` | chore: GitHub Actions CI skeleton |
| `59906ce` | feat(tools): generate-dataset --small produces 300 deterministic synthetic documents |
| HEAD | docs: README, architecture skeleton, chapter map, ADRs, phase-0 report |

Repo skeleton:

- `RAG-in-DotNet.slnx` (the new .NET 10 XML solution format) referencing 14 `src/` projects + 4 `tests/` projects + the `tools/generate-dataset` console.
- Central package management via `Directory.Packages.props`, with all NuGet pins **verified live on 2026-05-02**.
- `Directory.Build.props` enforcing nullable, `TreatWarningsAsErrors=true`, latest C#, deterministic builds, code-style enforcement, XML doc generation.
- `.editorconfig` with Roslyn defaults plus a `private static readonly` → PascalCase override (otherwise idiomatic lookup tables fail IDE1006).
- Docker Compose stack with healthchecks: Qdrant, Neo4j 5.28 Community + APOC, Redis 7, Ollama with sidecar that pulls `nomic-embed-text` + `llama3.2` once, and Aspire Dashboard.
- GitHub Actions CI matrix (`ubuntu-latest` × `windows-latest`): build → test → format-check, with `ci-eval.yml` placeholder for the Phase 6 faithfulness gate. Dependabot grouped weekly updates.
- 6-silo Contoso dataset generator (300 docs, ~0.5s wall time, byte-identical across runs).
- README with the Versions Covered box, status flags, full chapter map, quick start, and repo layout.
- 6 ADRs covering: ADR template, front-matter-on-day-1, syllabus-numbering wins, OllamaSharp-not-MS-Ollama, version drift from the brief, and .NET 10 template changes.

## Quality bar (per mission §7)

| Gate | Result |
|---|---|
| `dotnet build` | ✅ 0 warnings, 0 errors (`-warnaserror` enforced) |
| `dotnet test` | ✅ 1 passed, 0 failed |
| `dotnet format --verify-no-changes` | ✅ clean |
| Public APIs have XML docs | ✅ N/A — only `Placeholder` types exist in `src/`, all carry doc comments |
| Sample top-of-file chapter comments | ✅ N/A — no samples yet |
| Banned-API grep clean | ✅ see verification log below |
| README updated | ✅ |
| Phase report written | ✅ this file |

### Banned-API grep result

```
$ grep -rn -E "ChatAgent[^A-Za-z]|IVectorStore[^a-zA-Z]|IVectorStoreRecordCollection|CreateCollectionIfNotExistsAsync|VectorStoreRecordKey|VectorStoreRecordData|VectorStoreRecordVector|Microsoft\.Extensions\.AI\.Ollama" \
   src/ tests/ tools/
   (no matches)
```

(Run via the CI matrix on Phase 1 onward; in Phase 0 the source tree is small enough that a manual grep is decisive.)

## What was deferred

- **Bicep templates** (`infra/azure/`) — Phase 7 / Ch 25. Placeholder `.gitkeep` only.
- **`tools/eval-runner` and `tools/deepeval-bridge`** — Phase 6 / Ch 20. Not yet scaffolded.
- **Hello-World sample (`samples/Ch01_HelloWorldRag/`)** — Phase 1 / Ch 1.
- **`SmartDocs.Core` interfaces** (`IDocumentLoader`, `IChunker`, `IEmbeddingService`, `IVectorStore`, `IRetriever`) — Phase 1 / Ch 2.
- **`TokenCounter`** — Phase 1 / Ch 2.
- **Golden eval set** (`data/eval-sets/`) — Phase 2 / Ch 7.
- **`--large` (5 000-doc) dataset** — Phase 8 stretch goal. Currently throws `NotImplementedException`.

## What surprised us

1. **`Microsoft.Extensions.AI` jumped 9.x → 10.x already.** The mission brief had us pin 9.x but NuGet resolved 10.5.1 as the current GA. Same story for `M.E.Http.Resilience` and `M.E.Resilience`. ADR-0005 records the reconciliation.
2. **`Microsoft.ML.Tokenizers` jumped 0.22 → 2.0.0.** The 1.x line was effectively skipped between 0.22 and the 2.0 GA. Pin updated.
3. **`Neo4j.Driver` 6.0.0 GA shipped.** The brief and spec both reference 5.28.x. The driver follows SemVer (the *server* CalVer is the 2025.x line). Pin updated; ADR-0005 captures the migration note for Ch 13 sample code.
4. **`Microsoft.Agents.AI.A2A` is real but preview-only.** The user pointed out the `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` package; a search for prerelease A2A packages turned up the family at `1.3.0-preview.260423.1`. Pinned with the preview suffix; README still notes "A2A 1.0 support coming soon".
5. **`dotnet new sln` produces `.slnx`** (XML) on .NET 10 SDK by default, not `.sln`. ADR-0006 documents this.
6. **`blazorserver` template was removed** in .NET 10 in favor of unified `blazor` with `--interactivity Server`. ADR-0006 documents this.
7. **`<TreatWarningsAsErrors>true>` plus `<EnforceCodeStyleInBuild>true>` plus naming rules** trips IDE1006 on private static readonly fields without a `_` prefix. The fix in `.editorconfig` is to define a more specific rule that maps `private static readonly` to PascalCase before the generic `private readonly` → `_camelCase` rule.
8. **Docker is not installed on the build machine.** `infra/docker-compose.yml` ships **unverified on real Docker** — only the YAML is parsed. First reader to run `docker compose up -d` should report any breakage.

## Next phase

**Phase 1 — Foundations (Ch 1–2)**, gated on user approval per mission §6 "stop and ask". The first task in Phase 1 is the `samples/Ch01_HelloWorldRag/` ~80-line single-file program (in-memory `VectorStore`, hardcoded HR docs, brute-force cosine similarity, both Azure OpenAI and Ollama wired through DI configuration).
