using SmartDocs.Core.Documents;
using SmartDocs.Retrieval.VectorStores;

namespace SmartDocs.UnitTests.VectorStores;

public sealed class InMemoryVectorStoreTests
{
    private static EmbeddedChunk Make(string id, float[] vec, string text = "")
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        var chunk = new DocumentChunk(id + "#0", id, 0, text, 0, text.Length, meta);
        return new EmbeddedChunk(chunk, vec, "stub");
    }

    [Fact]
    public async Task Search_returns_top_k_by_cosine()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(new[]
        {
            Make("a", [1f, 0f, 0f]),
            Make("b", [0f, 1f, 0f]),
            Make("c", [0.9f, 0.1f, 0f]),
        });

        var results = await store.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f]), topK: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("a", results[0].Chunk.DocumentId);
        Assert.Equal("c", results[1].Chunk.DocumentId);
    }

    [Fact]
    public async Task Upsert_replaces_chunk_with_same_id()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync([Make("a", [1f, 0f, 0f], "v1")]);
        await store.UpsertAsync([Make("a", [1f, 0f, 0f], "v2")]);

        var results = await store.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f]), topK: 5);

        Assert.Single(results);
        Assert.Equal("v2", results[0].Chunk.Text);
    }

    [Fact]
    public async Task Delete_removes_specified_chunks()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(new[]
        {
            Make("a", [1f, 0f, 0f]),
            Make("b", [0f, 1f, 0f]),
        });

        await store.DeleteAsync(["a#0"]);
        var results = await store.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f]), topK: 5);

        Assert.Single(results);
        Assert.Equal("b", results[0].Chunk.DocumentId);
    }

    [Fact]
    public async Task EnsureCollectionExistsAsync_is_no_op()
    {
        var store = new InMemoryVectorStore("collection-name");
        await store.EnsureCollectionExistsAsync(); // Doesn't throw.
        Assert.Equal("collection-name", store.CollectionName);
    }
}
