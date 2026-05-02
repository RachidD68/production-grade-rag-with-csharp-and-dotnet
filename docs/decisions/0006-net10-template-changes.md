# 0006 — .NET 10 template changes: .slnx and unified Blazor

Date: 2026-05-02

## Status

Accepted

## Context

Two .NET 10 SDK template changes affected Phase 0 scaffolding:

1. **`dotnet new sln`** now produces an XML-based `.slnx` file by default instead of the legacy text `.sln` format. Both formats are supported by `dotnet`, MSBuild, Visual Studio 2026, Rider, and VS Code's C# extension. CI tooling pinned to a `.sln` filename will not find a `.slnx`.
2. **`blazorserver`** template was retired. The replacement is `blazor` (the unified "Blazor Web App") with `--interactivity Server`/`WebAssembly`/`Auto`. The new template emits the modern static SSR + interactive-island shell that the spec's "Blazor Server eval dashboard" description implies.

Stop-condition §8 specifically called out the `blazorserver` template potentially being missing; we hit it.

## Decision

1. Use the new `.slnx` solution format. CI workflows reference `RAG-in-DotNet.slnx` explicitly. README documents the format in the "Repository Layout" tree.
2. Use `dotnet new blazor --interactivity Server` for `SmartDocs.Dashboard`. The default behavior matches the spec's "real-time display of retrieval and generation metrics" (Ch 20) — Server interactivity is correct for a dashboard fed by SignalR-pushed metrics.

## Consequences

**Positive**:
- The repo uses the modern, supported template. Future reader systems (.NET 10+ exclusively) work out of the box.
- `.slnx` parses faster, diffs cleaner in PRs, and supports nested folders without separate `*.slnFolder` IDs.

**Negative**:
- A reader on .NET 9 SDK cannot open the `.slnx`; `global.json` enforces .NET 10 SDK, so this is detected at `dotnet restore` time with a clear error message.
- Teams whose IDE caches `.sln` paths may need to update bookmarks.

**Neutral**:
- If a reader needs a `.sln`, `dotnet sln migrate` (or Visual Studio's "Save As") regenerates the legacy format from the `.slnx`.
