using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Embeddings;

namespace SmartDocs.UnitTests.Embeddings;

public sealed class BatchEmbeddingPipelineTests
{
    private static readonly float[] OneVec = [1f];
    private static readonly int[] BatchSizesExpected = [4, 4, 2];

    private static DocumentMetadata Meta() => new(
        "x", "x", "x", "x", "Internal", "Policy", 2026, "x",
        new DateOnly(2026, 1, 1), "x");

    [Fact]
    public async Task Batches_chunks_at_configured_size()
    {
        var batchSizes = new List<int>();
        var stub = new BatchAwareStubGenerator((batch) =>
        {
            batchSizes.Add(batch.Length);
            return batch.Select(_ => OneVec).ToArray();
        });

        var pipeline = new BatchEmbeddingPipeline(
            stub,
            "stub-model",
            new BatchEmbeddingOptions { BatchSize = 4, MaxRetries = 0 },
            NullLogger<BatchEmbeddingPipeline>.Instance);

        var chunks = Enumerable.Range(0, 10)
            .Select(i => new DocumentChunk($"c#{i}", "c", i, $"t{i}", 0, 1, Meta()))
            .ToArray();

        var collected = new List<EmbeddedChunk>();
        await foreach (var e in pipeline.EmbedAsync(ToAsync(chunks)))
        {
            collected.Add(e);
        }

        Assert.Equal(10, collected.Count);
        // 10 inputs / batch size 4 = batches of 4, 4, 2.
        Assert.Equal(BatchSizesExpected, batchSizes);
    }

    [Fact]
    public async Task Retries_on_transient_429_failure()
    {
        var attempts = 0;
        var stub = new BatchAwareStubGenerator((batch) =>
        {
            if (++attempts == 1)
            {
                throw new HttpRequestException("Simulated 429 from provider");
            }
            return batch.Select(_ => OneVec).ToArray();
        });

        var pipeline = new BatchEmbeddingPipeline(
            stub,
            "stub-model",
            new BatchEmbeddingOptions { BatchSize = 4, MaxRetries = 3 },
            NullLogger<BatchEmbeddingPipeline>.Instance);

        var chunk = new DocumentChunk("c#0", "c", 0, "t", 0, 1, Meta());
        var collected = new List<EmbeddedChunk>();
        await foreach (var e in pipeline.EmbedAsync(ToAsync(new[] { chunk })))
        {
            collected.Add(e);
        }

        Assert.Single(collected);
        Assert.Equal(2, attempts); // first failed, second succeeded
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> items)
    {
        foreach (var i in items) { await Task.Yield(); yield return i; }
    }

    /// <summary>Stub that gives the test full visibility into batch composition.</summary>
    private sealed class BatchAwareStubGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly Func<string[], float[][]> _onBatch;

        public BatchAwareStubGenerator(Func<string[], float[][]> onBatch) { _onBatch = onBatch; }

        public EmbeddingGeneratorMetadata Metadata { get; } = new("batch-stub");

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var arr = values.ToArray();
            var vecs = _onBatch(arr);
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                vecs.Select(v => new Embedding<float>(v)).ToList()));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
