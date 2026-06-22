using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartDocs.Core.Conversations;
using SmartDocs.Performance.Conversations;

namespace SmartDocs.UnitTests.Conversations;

public sealed class ConversationStateStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static MemoryDistributedCache NewDistributedCache() =>
        new(Options.Create(new MemoryDistributedCacheOptions()));

    // --- In-memory store -----------------------------------------------------

    [Fact]
    public async Task InMemory_set_then_get_roundtrips_the_state()
    {
        var store = new InMemoryConversationStateStore(new MutableTimeProvider(Start));

        await store.SetAsync("c1", "thread-blob", TimeSpan.FromMinutes(30));
        var got = await store.GetAsync("c1");

        Assert.Equal("thread-blob", got);
    }

    [Fact]
    public async Task InMemory_get_unknown_id_returns_null()
    {
        var store = new InMemoryConversationStateStore(new MutableTimeProvider(Start));

        Assert.Null(await store.GetAsync("missing"));
    }

    [Fact]
    public async Task InMemory_entry_expires_after_its_ttl()
    {
        var clock = new MutableTimeProvider(Start);
        var store = new InMemoryConversationStateStore(clock);

        await store.SetAsync("c1", "blob", TimeSpan.FromMinutes(10));

        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal("blob", await store.GetAsync("c1")); // still inside the window

        clock.Advance(TimeSpan.FromMinutes(2)); // now 11 min — past the 10 min TTL
        Assert.Null(await store.GetAsync("c1"));
    }

    [Fact]
    public async Task InMemory_delete_removes_the_entry()
    {
        var store = new InMemoryConversationStateStore(new MutableTimeProvider(Start));
        await store.SetAsync("c1", "blob", TimeSpan.FromMinutes(30));

        await store.DeleteAsync("c1");

        Assert.Null(await store.GetAsync("c1"));
    }

    [Fact]
    public async Task InMemory_set_overwrites_an_existing_entry()
    {
        var store = new InMemoryConversationStateStore(new MutableTimeProvider(Start));

        await store.SetAsync("c1", "v1", TimeSpan.FromMinutes(30));
        await store.SetAsync("c1", "v2", TimeSpan.FromMinutes(30));

        Assert.Equal("v2", await store.GetAsync("c1"));
    }

    [Fact]
    public async Task InMemory_non_positive_ttl_is_rejected()
    {
        var store = new InMemoryConversationStateStore(new MutableTimeProvider(Start));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.SetAsync("c1", "blob", TimeSpan.Zero));
    }

    // --- Distributed (Redis-shaped) store ------------------------------------

    [Fact]
    public async Task Distributed_set_then_get_roundtrips_the_state()
    {
        var store = new DistributedConversationStateStore(NewDistributedCache());

        await store.SetAsync("c1", "thread-blob", TimeSpan.FromMinutes(30));

        Assert.Equal("thread-blob", await store.GetAsync("c1"));
    }

    [Fact]
    public async Task Distributed_get_unknown_id_returns_null()
    {
        var store = new DistributedConversationStateStore(NewDistributedCache());

        Assert.Null(await store.GetAsync("missing"));
    }

    [Fact]
    public async Task Distributed_delete_removes_the_entry()
    {
        var store = new DistributedConversationStateStore(NewDistributedCache());
        await store.SetAsync("c1", "blob", TimeSpan.FromMinutes(30));

        await store.DeleteAsync("c1");

        Assert.Null(await store.GetAsync("c1"));
    }

    [Fact]
    public async Task Distributed_keys_are_namespaced_so_two_ids_do_not_collide()
    {
        var cache = NewDistributedCache();
        var store = new DistributedConversationStateStore(cache);

        await store.SetAsync("a", "state-a", TimeSpan.FromMinutes(30));
        await store.SetAsync("b", "state-b", TimeSpan.FromMinutes(30));

        Assert.Equal("state-a", await store.GetAsync("a"));
        Assert.Equal("state-b", await store.GetAsync("b"));
    }

    // --- DI registration -----------------------------------------------------

    [Fact]
    public void AddInMemoryConversationState_registers_the_in_memory_store()
    {
        var services = new ServiceCollection();
        services.AddInMemoryConversationState();

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IConversationStateStore>();

        Assert.IsType<InMemoryConversationStateStore>(store);
    }

    [Fact]
    public void AddDistributedConversationState_registers_the_distributed_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDistributedCache>(NewDistributedCache());
        services.AddDistributedConversationState();

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IConversationStateStore>();

        Assert.IsType<DistributedConversationStateStore>(store);
    }
}
