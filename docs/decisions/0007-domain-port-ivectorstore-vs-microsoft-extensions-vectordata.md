# 0007 — Keep our own `IVectorStore` distinct from `Microsoft.Extensions.VectorData.VectorStore`

Date: 2026-05-02

## Status

Accepted

## Context

`Microsoft.Extensions.VectorData.Abstractions` 10.5.0 ships an abstract class `VectorStore` and a generic `VectorStoreCollection<TKey, TRecord>` that connector packages (Qdrant, Azure AI Search, Postgres, Redis, in-memory) target. That hierarchy is shaped to be storage-agnostic, polymorphic over key types, and rich enough to support the full Ch 6 indexing surface (filters, vector + hybrid search, metadata projections, lifecycle).

We could:

1. **Use `Microsoft.Extensions.VectorData` directly throughout SmartDocs** — every retriever, ingester, and citation tool consumes `VectorStore` and `VectorStoreCollection<…>` types.
2. **Keep our own domain port `SmartDocs.Core.Abstractions.IVectorStore`** — a SmartDocs-shaped interface, with concrete implementations (Phase 2) that delegate to `Microsoft.Extensions.VectorData` adapters underneath.

## Decision

Option 2 — keep the domain port. `IVectorStore` lives in `SmartDocs.Core/Abstractions/IVectorStore.cs` with operations that match how SmartDocs domain code wants to talk to a vector store: `EnsureCollectionExistsAsync`, `UpsertAsync(IEnumerable<EmbeddedChunk>)`, `SearchAsync(ReadOnlyMemory<float>, int) → IReadOnlyList<RetrievalResult>`, `DeleteAsync(IEnumerable<string>)`. No generics over key/record types — every chunk has a string `ChunkId` and `EmbeddedChunk` is the record shape.

Phase 2 (Ch 6) will introduce three adapters — `QdrantVectorStore`, `AzureAISearchVectorStore`, `InMemoryVectorStore` — each of which will take the corresponding `Microsoft.Extensions.VectorData` `VectorStore` (and its `VectorStoreCollection<string, EmbeddedChunkRecord>`) as a constructor dependency and translate calls.

## Consequences

**Positive**:
- Chapter samples and tests can mock a single, narrow interface — they don't need to learn the M.E.VectorData generic surface to write a fake.
- Decorator chains (Ch 8 hybrid retrieval, Ch 14 fusion, Ch 22 drift adapter, Ch 24 GDPR deletion) compose around our domain types directly.
- We can swap or augment the storage layer without touching the rest of the pipeline. If pgvector connectors mature first, or Azure Cosmos DB native hybrid becomes the production default, only the adapter changes.
- Aligns with mission §3: the banned API list bans `Microsoft.Extensions.VectorData.IVectorStore<…>` (the *old* interface) but the *new* `VectorStore` abstract class is fine. By owning our port, we never accidentally surface the M.E.VectorData type names to readers.

**Negative**:
- Two concepts named "vector store" in the same codebase. Mitigated by the clear naming: ours is `SmartDocs.Core.Abstractions.IVectorStore`, theirs is `Microsoft.Extensions.VectorData.VectorStore`. Adapters are explicit about both.
- A small amount of translation code in each adapter. Acceptable cost — the adapter ships once per connector and is exercised by Ch 6.

**Neutral**:
- If `Microsoft.Extensions.VectorData` ever ships exactly the operations SmartDocs needs (no generic ceremony, our metric fixed at the collection level), this ADR can be superseded and the domain port collapsed into the MS abstract class.
