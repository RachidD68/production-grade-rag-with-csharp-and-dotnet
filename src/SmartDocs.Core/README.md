# SmartDocs.Core

Core abstractions and primitives for the **SmartDocs** Retrieval-Augmented Generation
stack — the companion library to *Production-Grade RAG with C# and .NET*.

This is the foundation every other `SmartDocs.*` package builds on. It defines the
domain ports (`IRetriever`, `IVectorStore`, `IEmbeddingService`), the document model
(`DocumentChunk`, `RetrievalResult`, `DocumentMetadata`), configuration options, and
token counting — with no dependency on any specific vector database, LLM provider, or
hosting model.

The abstractions and document model are reflection-free; the reflection-based
configuration binding lives behind the `AddSmartDocsCore` DI helper, so trimmed/AOT
hosts can register options another way and keep the rest of the surface lean.

## Install

```bash
dotnet add package SmartDocs.Core
```

## License

MIT © Rachid Dahir
