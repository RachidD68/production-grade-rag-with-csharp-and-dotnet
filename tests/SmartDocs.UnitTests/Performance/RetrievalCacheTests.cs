using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Performance;

namespace SmartDocs.UnitTests.Performance;

public sealed class RetrievalCacheTests
{
    private static MemoryDistributedCache NewCache() =>
        new(Options.Create(new MemoryDistributedCacheOptions()));

    private static DocumentChunk Chunk(string id, string text)
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk(id + "#0", id, 0, text, 0, text.Length, meta);
    }

    [Fact]
    public async Task RetrieveAsync_miss_then_hit_skips_inner_retriever()
    {
        var inner = new CountingRetriever(q => [new RetrievalResult(Chunk("a", q), 0.9)]);
        var cache = new RetrievalCache(inner, NewCache());

        var first = await cache.RetrieveAsync("vacation", topK: 5);
        var second = await cache.RetrieveAsync("vacation", topK: 5);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal("a#0", second[0].Chunk.ChunkId);
        Assert.Equal(0.9, second[0].Score);
        Assert.Equal(1, inner.Calls); // The second call is served from cache.
    }

    [Fact]
    public async Task RetrieveAsync_keys_on_topK_so_different_topK_misses()
    {
        var inner = new CountingRetriever(q => [new RetrievalResult(Chunk("a", q), 0.5)]);
        var cache = new RetrievalCache(inner, NewCache());

        await cache.RetrieveAsync("q", topK: 3);
        await cache.RetrieveAsync("q", topK: 7); // Different topK ⇒ different key ⇒ miss.

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public void Strategy_wraps_inner_strategy()
    {
        var inner = new CountingRetriever(_ => []);
        var cache = new RetrievalCache(inner, NewCache());
        Assert.Equal("cached(counting)", cache.Strategy);
    }

    private sealed class CountingRetriever : IRetriever
    {
        private readonly Func<string, IReadOnlyList<RetrievalResult>> _impl;
        public int Calls { get; private set; }
        public CountingRetriever(Func<string, IReadOnlyList<RetrievalResult>> impl) => _impl = impl;
        public string Strategy => "counting";
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string q, int topK, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<RetrievalResult>>([.. _impl(q).Take(topK)]);
        }
    }
}
