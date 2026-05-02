using Qdrant.Client;
using SmartDocs.Core.Documents;
using SmartDocs.Retrieval.VectorStores;

namespace SmartDocs.IntegrationTests.Qdrant;

/// <summary>
/// Integration tests for <see cref="QdrantVectorStore"/>. Gated behind
/// <c>RUN_QDRANT_INTEGRATION=1</c> + a running Qdrant on
/// localhost:6334 (gRPC). When Docker is up via
/// <c>infra/docker-compose.yml</c>, both conditions are met.
/// </summary>
[Trait("Category", "RealQdrant")]
public sealed class QdrantVectorStoreTests
{
    private const string GateVar = "RUN_QDRANT_INTEGRATION";

    [Fact]
    public async Task Roundtrip_upsert_search_delete_against_real_qdrant()
    {
        if (Environment.GetEnvironmentVariable(GateVar) != "1")
        {
            Console.WriteLine("[SKIP] set RUN_QDRANT_INTEGRATION=1 + start docker compose to run");
            return;
        }

        var collection = $"smartdocs-itest-{Guid.NewGuid():N}";
        var client = new QdrantClient("localhost", port: 6334);
        var store = new QdrantVectorStore(client, collection, vectorSize: 3);

        try
        {
            await store.EnsureCollectionExistsAsync();

            var meta = new DocumentMetadata("doc-1", "hr-policies", "HR", "Montreal",
                "Internal", "Policy", 2026, "Author", new DateOnly(2026, 1, 1), "Test");
            var c1 = new EmbeddedChunk(
                new DocumentChunk("doc-1#0", "doc-1", 0, "first chunk text", 0, 16, meta),
                new ReadOnlyMemory<float>([1f, 0f, 0f]),
                "test-model");
            var c2 = new EmbeddedChunk(
                new DocumentChunk("doc-1#1", "doc-1", 1, "second chunk text", 16, 33, meta),
                new ReadOnlyMemory<float>([0f, 1f, 0f]),
                "test-model");

            await store.UpsertAsync([c1, c2]);

            var hits = await store.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f]), topK: 2);

            Assert.Equal(2, hits.Count);
            Assert.Equal("doc-1#0", hits[0].Chunk.ChunkId);
            Assert.Equal("first chunk text", hits[0].Chunk.Text);
            // Metadata roundtrip
            Assert.Equal("HR", hits[0].Chunk.Metadata.Department);
            Assert.Equal(2026, hits[0].Chunk.Metadata.FiscalYear);

            await store.DeleteAsync(["doc-1#0"]);
            var afterDelete = await store.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f]), topK: 2);
            Assert.Single(afterDelete);
            Assert.Equal("doc-1#1", afterDelete[0].Chunk.ChunkId);
        }
        finally
        {
            try { await client.DeleteCollectionAsync(collection); } catch { /* best-effort cleanup */ }
            client.Dispose();
        }
    }
}
