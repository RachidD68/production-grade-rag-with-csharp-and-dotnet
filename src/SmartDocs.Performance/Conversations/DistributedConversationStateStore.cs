using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using SmartDocs.Core.Conversations;

namespace SmartDocs.Performance.Conversations;

/// <summary>
/// The production hot store for conversation state (Ch 25): an
/// <see cref="IConversationStateStore"/> over <see cref="IDistributedCache"/>,
/// which is Redis in production (wired by
/// <c>AddStackExchangeRedisCache</c>) and an in-memory distributed cache in the
/// dev inner loop. The session blob is stored UTF-8 encoded under a namespaced
/// key, with the TTL applied as the cache entry's absolute expiry — so an
/// expired session simply disappears from Redis, no sweeper required.
///
/// <para>
/// Redis is the <em>hot</em> tier; the same <see cref="IConversationStateStore"/>
/// seam fronts a <em>durable</em> tier — Azure Cosmos DB — for sessions that must
/// outlive the cache (the Bicep provisions a Cosmos account for exactly this).
/// A Cosmos-backed implementation would live beside this one and be composed in
/// front of it (durable read-through), with no change to any caller; this class
/// deliberately does not take a Cosmos dependency so the package stays
/// package-free (<c>Microsoft.Azure.Cosmos</c> is intentionally NOT referenced).
/// </para>
///
/// <para>
/// The TTL is the consent / retention boundary (Ch 24), passed by the caller on
/// every write; an erasure request maps to <see cref="DeleteAsync"/>, which
/// removes the key from the hot tier immediately.
/// </para>
/// </summary>
public sealed class DistributedConversationStateStore : IConversationStateStore
{
    // Namespacing keeps conversation entries from colliding with the response /
    // retrieval caches that share the same Redis instance (Ch 21).
    private const string KeyPrefix = "conv:";

    private readonly IDistributedCache _cache;

    /// <summary>Create the store over a distributed <paramref name="cache"/> (Redis in production).</summary>
    /// <param name="cache">The backing distributed cache.</param>
    public DistributedConversationStateStore(IDistributedCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        var bytes = await _cache.GetAsync(KeyFor(conversationId), cancellationToken).ConfigureAwait(false);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    /// <inheritdoc />
    public Task SetAsync(
        string conversationId,
        string state,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);

        var bytes = Encoding.UTF8.GetBytes(state);
        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
        return _cache.SetAsync(KeyFor(conversationId), bytes, options, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        return _cache.RemoveAsync(KeyFor(conversationId), cancellationToken);
    }

    private static string KeyFor(string conversationId) => KeyPrefix + conversationId;
}
