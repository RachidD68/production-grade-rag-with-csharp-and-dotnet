using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;

namespace SmartDocs.Performance;

/// <summary>
/// Signal that a document changed (was re-ingested, edited, or deleted) and any
/// cached answers or retrievals derived from it are now stale.
/// </summary>
/// <param name="DocumentId">The id of the document whose content changed.</param>
public sealed record DocumentChangedEvent(string DocumentId);

/// <summary>
/// Evicts cached entries that depend on a document when that document changes.
/// The response and retrieval caches (Ch 21, Layers 1–2) can otherwise serve a
/// stale answer indefinitely — a cache hit hides every retrieval-side change.
/// This is the missing piece the chapter's Warning box describes.
/// </summary>
public interface ICacheInvalidator
{
    /// <summary>
    /// Record that the cache entry <paramref name="cacheKey"/> was produced
    /// using content from <paramref name="documentId"/>, so a later change to
    /// that document evicts this entry.
    /// </summary>
    void Track(string documentId, string cacheKey);

    /// <summary>Evict every tracked entry that depends on the changed document.</summary>
    Task InvalidateAsync(DocumentChangedEvent changed, CancellationToken cancellationToken = default);
}

/// <summary>
/// A minimal event-driven <see cref="ICacheInvalidator"/>. Because
/// <see cref="IDistributedCache"/> exposes no key enumeration, it maintains an
/// in-process reverse index from document id to the cache keys derived from it;
/// on a <see cref="DocumentChangedEvent"/> it removes each tracked key from the
/// cache. A real deployment would persist this index (or use Redis key tags /
/// a keyspace-notification listener) so it survives a restart — the contract is
/// the same.
/// </summary>
public sealed class CacheInvalidator : ICacheInvalidator
{
    private readonly IDistributedCache _cache;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _keysByDocument =
        new(StringComparer.Ordinal);

    /// <summary>Create an invalidator that evicts from <paramref name="cache"/>.</summary>
    public CacheInvalidator(IDistributedCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    /// <inheritdoc />
    public void Track(string documentId, string cacheKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        var keys = _keysByDocument.GetOrAdd(documentId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        keys[cacheKey] = 0;
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(DocumentChangedEvent changed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changed);
        if (!_keysByDocument.TryRemove(changed.DocumentId, out var keys))
        {
            return;
        }

        foreach (var key in keys.Keys)
        {
            await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
    }
}
