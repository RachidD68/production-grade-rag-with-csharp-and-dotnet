using Microsoft.Extensions.Caching.Memory;
using SmartDocs.Performance;

namespace SmartDocs.UnitTests.Performance;

public sealed class PerformanceTests
{
    [Fact]
    public async Task EmbeddingCache_skips_inner_call_on_repeat_input()
    {
        int calls = 0;
        var inner = new StubEmbeddingGenerator(_ =>
        {
            calls++;
            return [1f, 0f, 0f];
        });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var cached = new EmbeddingCache(inner, cache);

        var first = await cached.GenerateAsync(["hello world"]);
        var second = await cached.GenerateAsync(["hello world"]);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(1, calls); // The second call hits the cache.
    }

    [Fact]
    public async Task EmbeddingCache_caches_per_input_in_a_mixed_batch()
    {
        var calls = new List<string>();
        var inner = new StubEmbeddingGenerator(s =>
        {
            calls.Add(s);
            return [1f, 0f];
        });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var cached = new EmbeddingCache(inner, cache);

        await cached.GenerateAsync(["a", "b"]);
        await cached.GenerateAsync(["b", "c"]); // 'b' is cached; only 'c' should hit inner.

        Assert.Equal(["a", "b", "c"], calls);
    }

    [Fact]
    public void QueryCache_set_get_round_trip()
    {
        var cache = new QueryCache(new MemoryCache(new MemoryCacheOptions()));
        var resp = new SmartDocs.Generation.RagResponse(
            Answer: "ok",
            Sources: [],
            LatencyMs: 100,
            Strategy: "stub");

        cache.Set("Hello?", resp);
        var hit = cache.TryGet("hello?", out var got); // case- and whitespace-normalized

        Assert.True(hit);
        Assert.Same(resp, got);
    }

    [Fact]
    public void PollyPolicies_LlmApi_pipeline_builds_and_executes()
    {
        var pipeline = PollyPolicies.LlmApi();
        var result = pipeline.Execute(() => 42);
        Assert.Equal(42, result);
    }
}
