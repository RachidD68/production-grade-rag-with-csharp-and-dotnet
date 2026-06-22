# SmartDocs.Retrieval.AzureSearch

Azure AI Search adapters for the **SmartDocs** RAG stack — the companion library to
*Production-Grade RAG with C# and .NET*.

Provides two adapters over `Azure.Search.Documents`:

- `AzureAiSearchVectorStore` — an `IVectorStore` backed by an Azure AI Search index.
- `AzureAiSearchHybridRetriever` — vector + keyword search fused by Azure's built-in
  Reciprocal Rank Fusion, with an optional semantic ranker layered on top.

Install this package to retrieve from Azure AI Search; it brings the
`SmartDocs.Retrieval` engine with it.

## Install

```bash
dotnet add package SmartDocs.Retrieval.AzureSearch
```

## License

MIT © Rachid Dahir
