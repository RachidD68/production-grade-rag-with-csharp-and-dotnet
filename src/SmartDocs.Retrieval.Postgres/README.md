# SmartDocs.Retrieval.Postgres

Postgres + pgvector hybrid retriever for the **SmartDocs** RAG stack — the companion
library to *Production-Grade RAG with C# and .NET*.

Provides `PostgresHybridRetriever`, which runs dense ranking, full-text ranking, and
Reciprocal Rank Fusion inside a single SQL round-trip over Npgsql — the "the database
is the hybrid" pattern. Install this package to use Postgres as your hybrid store; it
brings the `SmartDocs.Retrieval` engine with it.

## Install

```bash
dotnet add package SmartDocs.Retrieval.Postgres
```

## License

MIT © Rachid Dahir
