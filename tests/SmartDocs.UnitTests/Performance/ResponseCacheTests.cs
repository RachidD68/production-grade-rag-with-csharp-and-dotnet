using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SmartDocs.Core.Documents;
using SmartDocs.Generation;
using SmartDocs.Performance;

namespace SmartDocs.UnitTests.Performance;

public sealed class ResponseCacheTests
{
    private static MemoryDistributedCache NewCache() =>
        new(Options.Create(new MemoryDistributedCacheOptions()));

    private static RagResponse Response(string answer) =>
        new(answer, [], LatencyMs: 5, Strategy: "stub");

    [Fact]
    public async Task AskAsync_miss_then_hit_skips_inner_pipeline()
    {
        var inner = new CountingPipeline(_ => Response("42 days"));
        var cache = new ResponseCache(inner, NewCache());

        var first = await cache.AskAsync("How many vacation days?");
        var second = await cache.AskAsync("How many vacation days?");

        Assert.Equal("42 days", first.Answer);
        Assert.Equal("42 days", second.Answer);
        Assert.Equal(1, inner.AskCalls); // The second call is served from cache.
    }

    [Fact]
    public async Task AskAsync_normalises_the_key_across_case_and_whitespace()
    {
        var inner = new CountingPipeline(_ => Response("answer"));
        var cache = new ResponseCache(inner, NewCache());

        await cache.AskAsync("  What's the Policy? ");
        await cache.AskAsync("what's the policy?");

        Assert.Equal(1, inner.AskCalls);
    }

    [Fact]
    public async Task AskStreamingAsync_hit_replays_sources_token_done_without_inner_call()
    {
        var sources = new List<RetrievalResult>
        {
            new(Chunk("a", "alpha"), 0.9),
        };
        var inner = new CountingPipeline(
            askResponse: _ => new RagResponse("the answer", sources, 5, "stub"));
        var cache = new ResponseCache(inner, NewCache());

        // Populate via AskAsync so the streaming path is a pure hit.
        await cache.AskAsync("q");

        var events = new List<RagStreamEvent>();
        await foreach (var ev in cache.AskStreamingAsync("q"))
        {
            events.Add(ev);
        }

        Assert.Equal(0, inner.StreamCalls); // Hit: inner stream never invoked.
        Assert.Equal(RagStreamEventKind.Sources, events[0].Kind);
        Assert.Single(events[0].Sources!);
        Assert.Equal(RagStreamEventKind.Token, events[1].Kind);
        Assert.Equal("the answer", events[1].Token);
        Assert.Equal(RagStreamEventKind.Done, events[2].Kind);
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public async Task AskStreamingAsync_miss_streams_inner_and_populates_cache()
    {
        var inner = new CountingPipeline(_ => Response("unused"));
        var cache = new ResponseCache(inner, NewCache());

        var streamed = new List<RagStreamEvent>();
        await foreach (var ev in cache.AskStreamingAsync("fresh"))
        {
            streamed.Add(ev);
        }

        Assert.Equal(1, inner.StreamCalls);
        Assert.Contains(streamed, e => e.Kind == RagStreamEventKind.Token);

        // After the miss, AskAsync is served from the populated cache.
        var hit = await cache.AskAsync("fresh");
        Assert.Equal("hello world", hit.Answer); // assembled from the inner token stream
        Assert.Equal(0, inner.AskCalls);
    }

    private static DocumentChunk Chunk(string id, string text)
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk(id + "#0", id, 0, text, 0, text.Length, meta);
    }

    /// <summary>An <see cref="IRagPipeline"/> that counts calls and streams a fixed two-token answer.</summary>
    private sealed class CountingPipeline : IRagPipeline
    {
        private readonly Func<string, RagResponse> _askResponse;
        public int AskCalls { get; private set; }
        public int StreamCalls { get; private set; }

        public CountingPipeline(Func<string, RagResponse> askResponse) => _askResponse = askResponse;

        public Task<RagResponse> AskAsync(string question, CancellationToken ct = default)
        {
            AskCalls++;
            return Task.FromResult(_askResponse(question));
        }

        public async IAsyncEnumerable<RagStreamEvent> AskStreamingAsync(
            string question,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            StreamCalls++;
            await Task.Yield();
            yield return new RagStreamEvent(RagStreamEventKind.Sources, Sources: []);
            yield return new RagStreamEvent(RagStreamEventKind.Token, Token: "hello ");
            yield return new RagStreamEvent(RagStreamEventKind.Token, Token: "world");
            yield return new RagStreamEvent(RagStreamEventKind.Done);
        }
    }
}
