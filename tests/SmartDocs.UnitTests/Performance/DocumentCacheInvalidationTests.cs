using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Generation;
using SmartDocs.Performance;

namespace SmartDocs.UnitTests.Performance;

/// <summary>
/// Ch 22 document → chunks → cache-entries invalidation, layered on the Ch 21
/// response/retrieval caches via <see cref="CachedDependencyTracker"/>.
/// </summary>
public sealed class DocumentCacheInvalidationTests
{
    private static readonly float[] EmbeddingVector = [0.1f, 0.2f];

    private static MemoryDistributedCache NewCache() =>
        new(Options.Create(new MemoryDistributedCacheOptions()));

    private static DocumentChunk Chunk(string docId, int index, string text)
    {
        var meta = new DocumentMetadata(docId, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk($"{docId}#{index}", docId, index, text, 0, text.Length, meta);
    }

    [Fact]
    public async Task InvalidateForDocument_evicts_cited_entries_and_leaves_others()
    {
        var distributed = NewCache();
        var tracker = new CachedDependencyTracker();

        // Retrieval cache citing doc-1's chunk; and a response cache citing doc-1.
        var retrieval = new RetrievalCache(
            new StubRetriever([new RetrievalResult(Chunk("doc-1", 0, "alpha"), 0.9)]),
            distributed, ttl: null, dependencies: tracker);
        var response = new ResponseCache(
            new StubPipeline(new RagResponse("answer", [new RetrievalResult(Chunk("doc-1", 0, "alpha"), 0.9)], 1, "stub")),
            distributed, ttl: null, dependencies: tracker);

        // An unrelated retrieval entry citing doc-2.
        var unrelated = new RetrievalCache(
            new StubRetriever([new RetrievalResult(Chunk("doc-2", 0, "zulu"), 0.8)]),
            distributed, ttl: null, dependencies: tracker);

        // Populate all three (store registers cited chunks with the tracker).
        await retrieval.RetrieveAsync("q-doc1", topK: 5);
        await response.AskAsync("question about doc1");
        await unrelated.RetrieveAsync("q-doc2", topK: 5);

        // Sanity: the doc-1 retrieval entry exists.
        Assert.NotNull(await distributed.GetAsync("retr:5:q-doc1"));

        var invalidator = new CacheInvalidator(distributed, tracker);
        await invalidator.InvalidateForDocumentAsync("doc-1");

        // doc-1's response + retrieval entries are gone.
        Assert.Null(await distributed.GetAsync("retr:5:q-doc1"));
        Assert.Null(await distributed.GetAsync("resp:question about doc1"));
        // The unrelated doc-2 entry survives.
        Assert.NotNull(await distributed.GetAsync("retr:5:q-doc2"));
    }

    [Fact]
    public async Task InvalidateForDocument_does_not_touch_the_content_keyed_embedding_cache()
    {
        var distributed = NewCache();
        var tracker = new CachedDependencyTracker();

        // A content-addressable embedding cache entry (SHA-keyed) for the same text.
        using var memory = new MemoryCache(new MemoryCacheOptions());
        const string EmbeddingKey = "emb:deadbeef";
        memory.Set(EmbeddingKey, EmbeddingVector);

        var retrieval = new RetrievalCache(
            new StubRetriever([new RetrievalResult(Chunk("doc-1", 0, "alpha"), 0.9)]),
            distributed, ttl: null, dependencies: tracker);
        await retrieval.RetrieveAsync("q", topK: 3);

        var invalidator = new CacheInvalidator(distributed, tracker);
        await invalidator.InvalidateForDocumentAsync("doc-1");

        // Retrieval entry evicted; the embedding cache is content-keyed and untouched.
        Assert.Null(await distributed.GetAsync("retr:3:q"));
        Assert.True(memory.TryGetValue(EmbeddingKey, out _));
    }

    [Fact]
    public async Task InvalidateForDocument_without_tracker_throws()
    {
        var invalidator = new CacheInvalidator(NewCache());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => invalidator.InvalidateForDocumentAsync("doc-1"));
    }

    private sealed class StubRetriever : IRetriever
    {
        private readonly IReadOnlyList<RetrievalResult> _results;
        public StubRetriever(IReadOnlyList<RetrievalResult> results) => _results = results;
        public string Strategy => "stub";
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string q, int topK, CancellationToken ct = default) =>
            Task.FromResult(_results);
    }

    private sealed class StubPipeline : IRagPipeline
    {
        private readonly RagResponse _response;
        public StubPipeline(RagResponse response) => _response = response;
        public Task<RagResponse> AskAsync(string question, CancellationToken ct = default) =>
            Task.FromResult(_response);

        public async IAsyncEnumerable<RagStreamEvent> AskStreamingAsync(
            string question,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new RagStreamEvent(RagStreamEventKind.Done);
        }
    }
}
