using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Embeddings;

namespace SmartDocs.UnitTests.Embeddings;

public sealed class EmbeddingServiceTests
{
    private static DocumentMetadata Meta() => new(
        "hr-001", "hr-policies", "HR", "Montreal", "Internal", "Policy",
        2026, "Author", new DateOnly(2026, 1, 1), "Title");

    /// <summary>Stub that records every string handed to the generator.</summary>
    private sealed class RecordingStub : IEmbeddingGenerator<string, Embedding<float>>
    {
        private static readonly float[] Vec = [1f, 2f, 3f];

        public List<string> Received { get; } = [];

        public EmbeddingGeneratorMetadata Metadata { get; } = new("recording-stub");

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var list = values.Select(v =>
            {
                Received.Add(v);
                return new Embedding<float>(Vec);
            }).ToList();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

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

    [Fact]
    public async Task Document_prefix_reaches_the_generator()
    {
        var stub = new RecordingStub();
        var svc = new EmbeddingService(
            stub, "stub-model", 3, NullLogger<EmbeddingService>.Instance, EmbeddingPrompt.Nomic);
        var chunk = new DocumentChunk("hr-001#0", "hr-001", 0, "vacation policy", 0, 14, Meta());

        await svc.EmbedAsync(chunk);

        Assert.Single(stub.Received);
        Assert.Equal("search_document: vacation policy", stub.Received[0]);
    }

    [Fact]
    public async Task Query_prefix_reaches_the_generator()
    {
        var stub = new RecordingStub();
        var svc = new EmbeddingService(
            stub, "stub-model", 3, NullLogger<EmbeddingService>.Instance, EmbeddingPrompt.Nomic);

        await svc.EmbedQueryAsync("how many vacation days?");

        Assert.Single(stub.Received);
        Assert.Equal("search_query: how many vacation days?", stub.Received[0]);
    }

    [Fact]
    public async Task None_prompt_passes_text_through_unchanged()
    {
        var stub = new RecordingStub();
        // No prompt argument -> EmbeddingPrompt.None default.
        var svc = new EmbeddingService(stub, "stub-model", 3, NullLogger<EmbeddingService>.Instance);
        var chunk = new DocumentChunk("hr-001#0", "hr-001", 0, "raw text", 0, 8, Meta());

        await svc.EmbedAsync(chunk);
        await svc.EmbedQueryAsync("raw query");

        Assert.Equal(["raw text", "raw query"], stub.Received);
    }

    [Fact]
    public async Task EmbedQueryAsync_returns_the_query_vector()
    {
        var stub = new StubEmbeddingGenerator(_ => [0.1f, 0.2f]);
        var svc = new EmbeddingService(stub, "stub-model", 2, NullLogger<EmbeddingService>.Instance);

        var vec = await svc.EmbedQueryAsync("anything");

        Assert.Equal(2, vec.Length);
        Assert.Equal(0.1f, vec.Span[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task EmbedQueryAsync_rejects_empty_or_whitespace(string? query)
    {
        var stub = new StubEmbeddingGenerator(_ => [1f]);
        var svc = new EmbeddingService(stub, "stub-model", 1, NullLogger<EmbeddingService>.Instance);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => svc.EmbedQueryAsync(query!));
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) { await Task.Yield(); yield return i; }
    }
}
