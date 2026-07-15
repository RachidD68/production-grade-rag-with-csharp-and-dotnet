using Microsoft.Extensions.Caching.Memory;
using SmartDocs.Generation;

namespace SmartDocs.Performance;

/// <summary>
/// Caches full <see cref="RagResponse"/> objects keyed by the normalized
/// query text. Configurable TTL — defaults to 10 minutes. Use sparingly:
/// the answer-cache hit hides any retrieval-side change (a cache flush is
/// part of every reindex).
/// </summary>
public sealed class QueryCache
{
    private readonly IMemoryCache _cache;
    public TimeSpan Ttl { get; }

    public QueryCache(IMemoryCache cache, TimeSpan? ttl = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
        Ttl = ttl ?? TimeSpan.FromMinutes(10);
    }

    public bool TryGet(string query, out RagResponse? response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return _cache.TryGetValue(NormalizeKey(query), out response);
    }

    public void Set(string query, RagResponse response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(response);
        _cache.Set(NormalizeKey(query), response, Ttl);
    }

    private static string NormalizeKey(string query)
    {
        return "q:" + query.Trim().ToLowerInvariant();
    }
}
