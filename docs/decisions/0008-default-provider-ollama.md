# 0008 — Default LLM provider is Ollama, not Azure OpenAI

Date: 2026-05-02

## Status

Accepted

## Context

The mission brief asks every chapter that calls an LLM or embedding model to provide both Ollama and Azure OpenAI wiring through configuration, and that local development should be possible without an Azure subscription. Both providers can be made to work with the same `IChatClient` / `IEmbeddingGenerator` abstractions; the only question is which one is the default in `appsettings.json`.

Reasons to default to **Azure OpenAI**:
- Production target throughout the book.
- Higher quality answers; reflects what most real systems will use.

Reasons to default to **Ollama**:
- Works offline. No API key. No subscription. No spend.
- Matches the local-only `infra/docker-compose.yml` stack — Ollama is included, Azure isn't.
- Failure mode for first-time readers is "service not running" (clear) rather than "401 Unauthorized" or "429 Quota Exceeded" (cryptic).
- Avoids a class of "I don't have an Azure account" support questions.

## Decision

Default `SmartDocs:Llm:Provider` is `Ollama`. `SmartDocs.Api/appsettings.json` and every sample's `appsettings.json` ship with the Ollama defaults pointing at `http://localhost:11434` with `llama3.2` (chat) + `nomic-embed-text` (embeddings) — the two models the `infra/docker-compose.yml` `ollama-init` sidecar pre-pulls.

Switching to Azure OpenAI is a config-only change — set:

```jsonc
{
  "SmartDocs": {
    "Llm": {
      "Provider": "AzureOpenAI",
      "Endpoint": "https://YOUR-AOAI.openai.azure.com/",
      "ApiKey":   "<from user-secrets or env var>",
      "ChatModel":      "gpt-4o-mini-deployment",
      "EmbeddingModel": "text-embedding-3-small-deployment"
    }
  }
}
```

Or via environment variables: `SmartDocs__Llm__Provider=AzureOpenAI`, `SmartDocs__Llm__ApiKey=…`, etc.

`AddSmartDocsCore()`'s validation ensures `ApiKey` is present when `Provider == AzureOpenAI`; missing it throws `OptionsValidationException` at startup.

## Consequences

**Positive**:
- A new reader can `git clone`, `docker compose up -d`, and run any chapter's sample without obtaining an API key.
- Tests stay free of secret-management ceremony.
- Costs nothing to experiment.

**Negative**:
- Output quality from `llama3.2` is lower than `gpt-4o`. Some chapters' demonstrations (faithfulness, multi-step reasoning) are only fully impressive on the Azure path. Each affected chapter calls this out and recommends switching for the relevant section.
- Embedding dimensions differ between providers (`nomic-embed-text` is 768; `text-embedding-3-small` is 1536). Vector store collections are dimension-bound, so flipping providers requires re-embedding the corpus. Ch 22 (model migration) makes this an explicit lesson rather than an accident.

**Neutral**:
- A future ADR may flip the default if a non-Azure cloud provider becomes more permissive on free-tier embedding access.
