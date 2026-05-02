# 0004 — Use OllamaSharp, never Microsoft.Extensions.AI.Ollama

Date: 2026-05-02

## Status

Accepted

## Context

There are two ways to talk to a local Ollama server from .NET:

1. **`Microsoft.Extensions.AI.Ollama`** — a Microsoft-published adapter that surfaced Ollama as `IChatClient` and `IEmbeddingGenerator`. It was deprecated in 2025.
2. **`OllamaSharp` (`OllamaApiClient`)** — a community library that implements both `IChatClient` *and* `IEmbeddingGenerator` (the same `Microsoft.Extensions.AI` abstractions), and is actively maintained.

Mission-brief §3 lists `Microsoft.Extensions.AI.Ollama` in the "Never use" column.

## Decision

`Microsoft.Extensions.AI.Ollama` is **forbidden**. The project will use `OllamaSharp.OllamaApiClient` everywhere a local Ollama integration is needed (Hello-World in Ch 1, fallback embedding/chat in Ch 21, etc.).

DI registration will look like:

```csharp
services.AddSingleton<IChatClient>(sp =>
    new OllamaApiClient(new Uri("http://localhost:11434"), "llama3.2"));
services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
    new OllamaApiClient(new Uri("http://localhost:11434"), "nomic-embed-text"));
```

`Directory.Packages.props` only pins `OllamaSharp`. CI's banned-API grep enforces the rule:

```
! grep -RIn "Microsoft\.Extensions\.AI\.Ollama" src/ tests/ tools/
```

## Consequences

- A single, supported library powers every local-LLM scenario in the book.
- Readers running the code in 2026 and beyond will not fail with "package not found" or unmaintained behaviors.
- We accept that `OllamaSharp` is community-maintained rather than Microsoft-owned. The fallback if it ever becomes unmaintained is to re-evaluate `Microsoft.Extensions.AI.OpenAI`'s base-URL override against an Ollama OpenAI-compatible endpoint.
