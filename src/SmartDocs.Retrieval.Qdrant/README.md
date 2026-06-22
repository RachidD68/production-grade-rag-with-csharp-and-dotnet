# SmartDocs.Retrieval.Qdrant

Qdrant vector-store adapter for the **SmartDocs** RAG stack — the companion library to
*Production-Grade RAG with C# and .NET*.

Provides `QdrantVectorStore`, an `IVectorStore` implementation over the official
`Qdrant.Client` gRPC client, and the `AddSmartDocsQdrantVectorStore` DI helper.
Install this package to retrieve from Qdrant; it brings the `SmartDocs.Retrieval`
engine with it.

## Install

```bash
dotnet add package SmartDocs.Retrieval.Qdrant
```

## License

MIT © Rachid Dahir
