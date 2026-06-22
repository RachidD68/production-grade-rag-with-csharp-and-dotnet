# SmartDocs.Reranking

Reranking layer for the **SmartDocs** RAG stack — the companion library to
*Production-Grade RAG with C# and .NET*.

The `IReranker` abstraction, a no-op control, an LLM reranker, and the Scrutor-based
reranking middleware that transparently decorates an `IRetriever` so reranking is
invisible to the rest of the pipeline.

Heavier rerankers ship as separate, opt-in packages:

- `SmartDocs.Reranking.Onnx` — self-hosted ONNX cross-encoder (bge-reranker-v2-m3)
- `SmartDocs.Reranking.Cohere` — Cohere Rerank API adapter

## Install

```bash
dotnet add package SmartDocs.Reranking
```

## License

MIT © Rachid Dahir
