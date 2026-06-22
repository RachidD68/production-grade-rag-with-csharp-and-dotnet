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
    private readonly CachedDependencyTracker? _dependencies;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _keysByDocument =
        new(StringComparer.Ordinal);

    /// <summary>Create an invalidator that evicts from <paramref name="cache"/>.</summary>
    public CacheInvalidator(IDistributedCache cache)
        : this(cache, dependencies: null)
    {
    }

    /// <summary>
    /// Create an invalidator that evicts from <paramref name="cache"/> and, when a
    /// <paramref name="dependencies"/> tracker is supplied, supports the
    /// document → chunks → cache-entries path of
    /// <see cref="InvalidateForDocumentAsync"/> (Ch 22).
    /// </summary>
    /// <param name="cache">The distributed cache to evict from.</param>
    /// <param name="dependencies">
    /// The reverse-dependency index the response and retrieval caches register
    /// cited chunks with. Pass <see langword="null"/> for the Ch 21 document-key
    /// tracking only.
    /// </param>
    public CacheInvalidator(IDistributedCache cache, CachedDependencyTracker? dependencies)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
        _dependencies = dependencies;
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

    /// <summary>
    /// Evict every Layer-1 response-cache and Layer-2 retrieval-cache entry that
    /// cited a chunk of <paramref name="documentId"/> (Ch 22). The path is
    /// document → chunks → cache-entries: it resolves the document's chunk ids via
    /// the <see cref="CachedDependencyTracker"/>, drains the cache keys those chunks
    /// were cited by, and removes them.
    ///
    /// <para>
    /// The content-keyed embedding cache is deliberately left alone: its entries are
    /// content-addressable (SHA of the text), so a chunk whose text is unchanged
    /// keeps a valid embedding even when the document around it changed — re-embedding
    /// it would be wasted work. (A summary / community cache, when one is in scope,
    /// is invalidated through the same drained-key path, since its entries register
    /// their constituent chunks with the tracker exactly like the other layers.)
    /// </para>
    /// </summary>
    /// <param name="documentId">The id of the document whose derived entries are now stale.</param>
    /// <param name="cancellationToken">Cancels the eviction.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the invalidator was created without a <see cref="CachedDependencyTracker"/>,
    /// since the document → chunks → entries path needs one.
    /// </exception>
    public async Task InvalidateForDocumentAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (_dependencies is null)
        {
            throw new InvalidOperationException(
                "InvalidateForDocumentAsync requires a CachedDependencyTracker; construct CacheInvalidator with one.");
        }

        var chunkIds = _dependencies.ChunksForDocument(documentId);
        var keys = _dependencies.DrainKeysForChunks(chunkIds);
        foreach (var key in keys)
        {
            await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
    }
}
