using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Performance;

/// <summary>
/// Layer 2 of the caching stack (Ch 21): a retrieval cache. Decorates an inner
/// <see cref="IRetriever"/> and serves the ranked
/// <see cref="RetrievalResult"/> list from an <see cref="IDistributedCache"/> on
/// a repeat of the same (query, topK) pair — skipping the embedding call and
/// the vector-store round-trip. Cheaper to populate than the response cache
/// (Layer 1) and it survives a change in the generation prompt, so it hits more
/// often: two questions that retrieve the same context share the entry even
/// when their answers differ.
/// <para>
/// The canonical <see cref="IRetriever.RetrieveAsync"/> has no filter
/// parameter, so the cache key is exactly the normalised query plus
/// <c>topK</c> — there is no hidden filter dimension to leak.
/// </para>
/// </summary>
public sealed class RetrievalCache : IRetriever
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IRetriever _inner;
    private readonly IDistributedCache _cache;

    /// <summary>The time-to-live applied to cached retrieval results.</summary>
    public TimeSpan Ttl { get; }

    /// <summary>Create a retrieval cache over <paramref name="inner"/> backed by <paramref name="cache"/>.</summary>
    /// <param name="inner">The retriever to invoke on a cache miss.</param>
    /// <param name="cache">The distributed cache holding serialised result lists.</param>
    /// <param name="ttl">Time-to-live for cached entries. Defaults to 5 minutes.</param>
    public RetrievalCache(IRetriever inner, IDistributedCache cache, TimeSpan? ttl = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        _inner = inner;
        _cache = cache;
        Ttl = ttl ?? TimeSpan.FromMinutes(5);
    }

    /// <inheritdoc />
    public string Strategy => $"cached({_inner.Strategy})";

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var key = KeyFor(query, topK);
        var bytes = await _cache.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (bytes is not null)
        {
            var cached = JsonSerializer.Deserialize<List<RetrievalResult>>(bytes, SerializerOptions);
            if (cached is not null)
            {
                return cached;
            }
        }

        var fresh = await _inner.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.SerializeToUtf8Bytes(fresh, SerializerOptions);
        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl };
        await _cache.SetAsync(key, payload, options, cancellationToken).ConfigureAwait(false);
        return fresh;
    }

    private static string KeyFor(string query, int topK) =>
        $"retr:{topK}:{query.Trim().ToLowerInvariant()}";
}
