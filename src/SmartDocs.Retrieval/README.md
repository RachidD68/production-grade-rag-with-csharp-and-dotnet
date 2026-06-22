# SmartDocs.Retrieval

Retrieval engine for the **SmartDocs** RAG stack — the companion library to
*Production-Grade RAG with C# and .NET*.

Dense, sparse, and hybrid retrievers; Reciprocal Rank Fusion; MMR and context
ordering; query-enhancement decorators (HyDE, RAG-Fusion, CRAG, step-back);
graph and vectorless retrieval; and the in-memory vector store for tests and demos.

Backend-specific adapters ship as separate, opt-in packages so you only pull the
client SDK you actually use:

- `SmartDocs.Retrieval.Qdrant` — Qdrant vector store
- `SmartDocs.Retrieval.Postgres` — Postgres + pgvector single-store hybrid
- `SmartDocs.Retrieval.AzureSearch` — Azure AI Search vector store + hybrid retriever

## Install

```bash
dotnet add package SmartDocs.Retrieval
```

## License

MIT © Rachid Dahir
