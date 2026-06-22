using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SmartDocs.Performance;

namespace SmartDocs.UnitTests.Performance;

public sealed class CacheInvalidatorTests
{
    private static MemoryDistributedCache NewCache() =>
        new(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public async Task Publishing_a_change_evicts_the_tracked_entry()
    {
        var cache = NewCache();
        await cache.SetStringAsync("resp:what is in doc-1?", "stale answer");

        var invalidator = new CacheInvalidator(cache);
        invalidator.Track("doc-1", "resp:what is in doc-1?");

        // Sanity: the entry exists before the change.
        Assert.NotNull(await cache.GetStringAsync("resp:what is in doc-1?"));

        await invalidator.InvalidateAsync(new DocumentChangedEvent("doc-1"));

        // The entry is gone after the document-changed signal.
        Assert.Null(await cache.GetStringAsync("resp:what is in doc-1?"));
    }

    [Fact]
    public async Task A_change_only_evicts_entries_for_the_changed_document()
    {
        var cache = NewCache();
        await cache.SetStringAsync("k1", "v1");
        await cache.SetStringAsync("k2", "v2");

        var invalidator = new CacheInvalidator(cache);
        invalidator.Track("doc-1", "k1");
        invalidator.Track("doc-2", "k2");

        await invalidator.InvalidateAsync(new DocumentChangedEvent("doc-1"));

        Assert.Null(await cache.GetStringAsync("k1"));      // evicted
        Assert.Equal("v2", await cache.GetStringAsync("k2")); // untouched
    }

    [Fact]
    public async Task Invalidating_an_unknown_document_is_a_no_op()
    {
        var cache = NewCache();
        var invalidator = new CacheInvalidator(cache);

        // Must not throw.
        await invalidator.InvalidateAsync(new DocumentChangedEvent("never-seen"));
    }
}
