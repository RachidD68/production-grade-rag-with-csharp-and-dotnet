# SmartDocs.Reranking.Cohere

Cohere Rerank adapter for the **SmartDocs** RAG stack — the companion library to
*Production-Grade RAG with C# and .NET*.

Provides `CohereReranker`, which calls Cohere's v2 Rerank API (`rerank-v3.5`) to
re-score retrieved candidates. It is a framework-only typed `HttpClient` client — no
Cohere SDK dependency. Set `COHERE_API_KEY` (or pass the key explicitly) and install
this package to rerank with Cohere; it brings the `SmartDocs.Reranking` layer with it.

## Install

```bash
dotnet add package SmartDocs.Reranking.Cohere
```

## License

MIT © Rachid Dahir
