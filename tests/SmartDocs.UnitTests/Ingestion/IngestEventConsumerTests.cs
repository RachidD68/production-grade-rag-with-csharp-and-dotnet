using Microsoft.Extensions.Logging.Abstractions;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Events;
using SmartDocs.Retrieval.VectorStores;

namespace SmartDocs.UnitTests.Ingestion;

/// <summary>Ch 22 ingest event consumer: tenant scope, version-guarded ordering, idempotency.</summary>
public sealed class IngestEventConsumerTests
{
    private static DocumentChunk Chunk(string docId, int index, string text)
    {
        var meta = new DocumentMetadata(docId, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk($"{docId}#{index}", docId, index, text, 0, text.Length, meta);
    }

    [Fact]
    public async Task Processing_an_event_reingests_and_invalidates()
    {
        var reingest = new RecordingReingest();
        var invalidated = new List<string>();
        var consumer = new IngestEventConsumer(
            new ChannelChangeFeed(),
            reingest,
            (docId, _) => { invalidated.Add(docId); return Task.CompletedTask; },
            NullLogger<IngestEventConsumer>.Instance);

        var handled = await consumer.ProcessAsync(new DocumentChanged("doc-1", "tenant-a", 1), default);

        Assert.True(handled);
        Assert.Equal(["doc-1"], reingest.Reingested);
        Assert.Equal(["doc-1"], invalidated);
    }

    [Fact]
    public async Task A_stale_version_event_is_skipped()
    {
        var reingest = new RecordingReingest();
        var consumer = new IngestEventConsumer(
            new ChannelChangeFeed(), reingest, (_, _) => Task.CompletedTask,
            NullLogger<IngestEventConsumer>.Instance);

        await consumer.ProcessAsync(new DocumentChanged("doc-1", "t", 5), default);
        // An older version arrives out of order — must be skipped.
        var handled = await consumer.ProcessAsync(new DocumentChanged("doc-1", "t", 3), default);

        Assert.False(handled);
        Assert.Equal(["doc-1"], reingest.Reingested); // only the first ran
    }

    [Fact]
    public async Task A_duplicate_delivery_is_deduped()
    {
        var reingest = new RecordingReingest();
        var consumer = new IngestEventConsumer(
            new ChannelChangeFeed(), reingest, (_, _) => Task.CompletedTask,
            NullLogger<IngestEventConsumer>.Instance);

        await consumer.ProcessAsync(new DocumentChanged("doc-1", "t", 7), default);
        // Exact same (document, version) redelivered — idempotent no-op.
        var handled = await consumer.ProcessAsync(new DocumentChanged("doc-1", "t", 7), default);

        Assert.False(handled);
        Assert.Single(reingest.Reingested);
    }

    [Fact]
    public async Task An_event_for_another_tenant_is_ignored()
    {
        var reingest = new RecordingReingest();
        var consumer = new IngestEventConsumer(
            new ChannelChangeFeed(), reingest, (_, _) => Task.CompletedTask,
            NullLogger<IngestEventConsumer>.Instance, tenantId: "tenant-a");

        var handled = await consumer.ProcessAsync(new DocumentChanged("doc-1", "tenant-b", 1), default);

        Assert.False(handled);
        Assert.Empty(reingest.Reingested);
    }

    [Fact]
    public async Task Reingest_removes_orphan_chunks_when_the_new_version_has_fewer()
    {
        // Index doc-1 with three chunks, then re-ingest a version that produces only
        // two — chunk #2 is an orphan and must be deleted, not left behind.
        var store = new InMemoryVectorStore();
        var embedder = new StubEmbeddingService();

        var initial = new[] { Chunk("doc-1", 0, "a"), Chunk("doc-1", 1, "b"), Chunk("doc-1", 2, "c") };
        foreach (var c in initial)
        {
            await store.UpsertAsync([await embedder.EmbedAsync(c)]);
        }

        IReadOnlyList<DocumentChunk> newVersion = [Chunk("doc-1", 0, "a"), Chunk("doc-1", 1, "b2")];
        var reingest = new DocumentReingestService(
            (_, _) => Task.FromResult(newVersion),
            embedder, store);
        reingest.RegisterExisting("doc-1", initial.Select(c => c.ChunkId));

        await reingest.ReingestAsync("doc-1");

        // Search returns at most the two surviving chunks; the orphan #2 is gone.
        var hits = await store.SearchAsync(new ReadOnlyMemory<float>([1f, 1f]), topK: 10);
        var ids = hits.Select(h => h.Chunk.ChunkId).OrderBy(x => x).ToArray();
        Assert.Equal(["doc-1#0", "doc-1#1"], ids);
        Assert.DoesNotContain("doc-1#2", ids);
    }

    private sealed class RecordingReingest : IDocumentReingestService
    {
        public List<string> Reingested { get; } = [];
        public Task ReingestAsync(string documentId, CancellationToken cancellationToken = default)
        {
            Reingested.Add(documentId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public string EmbeddingModel => "stub";
        public int Dimensions => 2;

        public Task<EmbeddedChunk> EmbedAsync(DocumentChunk chunk, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmbeddedChunk(chunk, new ReadOnlyMemory<float>([1f, 1f]), EmbeddingModel));

        public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReadOnlyMemory<float>([1f, 1f]));

        public async IAsyncEnumerable<EmbeddedChunk> EmbedAsync(
            IAsyncEnumerable<DocumentChunk> chunks,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in chunks.WithCancellation(cancellationToken))
            {
                yield return await EmbedAsync(chunk, cancellationToken);
            }
        }
    }
}
