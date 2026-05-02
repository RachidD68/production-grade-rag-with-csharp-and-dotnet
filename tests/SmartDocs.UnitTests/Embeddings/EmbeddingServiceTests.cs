using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Embeddings;

namespace SmartDocs.UnitTests.Embeddings;

public sealed class EmbeddingServiceTests
{
    private static DocumentMetadata Meta() => new(
        "hr-001", "hr-policies", "HR", "Montreal", "Internal", "Policy",
        2026, "Author", new DateOnly(2026, 1, 1), "Title");

    [Fact]
    public async Task Embed_single_chunk_records_model_name()
    {
        var stub = new StubEmbeddingGenerator(_ => [1f, 2f, 3f]);
        var svc = new EmbeddingService(stub, "stub-model", 3, NullLogger<EmbeddingService>.Instance);
        var chunk = new DocumentChunk("hr-001#0", "hr-001", 0, "text", 0, 4, Meta());

        var emb = await svc.EmbedAsync(chunk);

        Assert.Equal("stub-model", emb.EmbeddingModel);
        Assert.Equal(3, emb.Vector.Length);
        Assert.Same(chunk, emb.Chunk);
    }

    [Fact]
    public async Task Embed_stream_preserves_order()
    {
        var counter = 0;
        var stub = new StubEmbeddingGenerator(_ => [counter++]);
        var svc = new EmbeddingService(stub, "stub-model", 1, NullLogger<EmbeddingService>.Instance);

        var chunks = new[]
        {
            new DocumentChunk("a#0", "a", 0, "a", 0, 1, Meta()),
            new DocumentChunk("a#1", "a", 1, "b", 0, 1, Meta()),
            new DocumentChunk("a#2", "a", 2, "c", 0, 1, Meta()),
        };

        var collected = new List<EmbeddedChunk>();
        await foreach (var e in svc.EmbedAsync(ToAsync(chunks)))
        {
            collected.Add(e);
        }

        Assert.Equal(3, collected.Count);
        Assert.Equal("a#0", collected[0].Chunk.ChunkId);
        Assert.Equal("a#2", collected[2].Chunk.ChunkId);
    }

    [Fact]
    public void Constructor_validates_inputs()
    {
        var stub = new StubEmbeddingGenerator(_ => [1f]);
        var log = NullLogger<EmbeddingService>.Instance;

        Assert.Throws<ArgumentNullException>(() => new EmbeddingService(null!, "m", 1, log));
        Assert.Throws<ArgumentException>(() => new EmbeddingService(stub, "  ", 1, log));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmbeddingService(stub, "m", 0, log));
        Assert.Throws<ArgumentNullException>(() => new EmbeddingService(stub, "m", 1, null!));
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) { await Task.Yield(); yield return i; }
    }
}
