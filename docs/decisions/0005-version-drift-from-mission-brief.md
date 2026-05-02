# 0005 — Version drift between the mission brief and NuGet (May 2026)

Date: 2026-05-02

## Status

Accepted

## Context

The mission brief was authored before .NET 10 GA. Some of its pinned package versions were already obsolete by the time Phase 0 started. Stop-condition §8 requires us to surface the drift and pick a line.

| Package | Brief pin | NuGet GA on 2026-05-02 |
|---|---|---|
| `Microsoft.Extensions.AI` | `9.*` | `10.5.1` |
| `Microsoft.Extensions.Http.Resilience` | `9.*` | `10.5.0` |
| `Microsoft.Extensions.Resilience` | `9.*` | `10.5.0` |
| `Microsoft.ML.Tokenizers` | `0.22.*` | `2.0.0` |
| `Neo4j.Driver` | `5.28.*` | `6.0.0` |
| `Microsoft.Agents.AI.A2A` | `1.0.0-preview.*` | `1.3.0-preview.260423.1` (preview only — no GA yet) |

The brief's listed `Microsoft.Agents.AI` (1.3.x), `Microsoft.Agents.AI.Foundry` (1.3.x), `Microsoft.Extensions.VectorData.Abstractions` (10.x), `OllamaSharp` (latest), `Qdrant.Client` (1.17.x), `Polly` (8.x), `OpenTelemetry`/`xunit`/`Testcontainers`/`BenchmarkDotNet` are all unchanged.

## Decision

Pin to the **current GA versions on NuGet at the start of Phase 0** (2026-05-02), not the brief's literal pins. Specifically:

- `Microsoft.Extensions.AI` → `10.5.1`
- `Microsoft.Extensions.Http.Resilience` → `10.5.0`
- `Microsoft.Extensions.Resilience` → `10.5.0`
- `Microsoft.ML.Tokenizers` → `2.0.0`
- `Neo4j.Driver` → `6.0.0` (driver SemVer; the server is on CalVer 2025.x)
- `Microsoft.Agents.AI.A2A` → `1.3.0-preview.260423.1` (the only published version is preview; the README still notes "A2A 1.0 support coming soon")

The brief's literal version strings are treated as guidance, not contracts. The book's chapter text will be reviewed against the actual code in the corresponding phase report, and any wording that names a specific version will be flagged for an erratum.

## Consequences

**Positive**:
- Readers running `dotnet add package` in mid-2026 get the same versions as the repo. No "I get errors when I install this — the book says 9.x" discrepancy.
- We benefit from improvements in the 10.x line (notably faster prompt-caching, structured-output bug fixes, and the unified `Embedding<T>` shape).

**Negative**:
- Some chapter prose in the book may still reference 9.x APIs. The book author owns reconciling errata; this ADR is the record of the gap.
- `Neo4j.Driver` 6.0.0 dropped a couple of obsolete APIs from 5.x (`IRecord.AsDictionary` became `IRecord.Values`). Ch 13 sample code reflects 6.0.0; readers on 5.28.x must adapt.

**Neutral**:
- Versions are revisited at the start of every phase. If 10.5.1 is itself superseded by Phase 6, we re-pin without superseding this ADR (the ADR records *that* the drift was reconciled, not the specific version chosen — that's the truth of `Directory.Packages.props`).
