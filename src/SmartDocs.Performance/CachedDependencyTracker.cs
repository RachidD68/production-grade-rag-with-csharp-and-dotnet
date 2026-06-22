using System.Collections.Concurrent;

namespace SmartDocs.Performance;

/// <summary>
/// The reverse-dependency index behind document-scoped cache invalidation
/// (Ch 22). It answers the two questions <see cref="CacheInvalidator"/> needs
/// when a document changes:
/// <list type="number">
///   <item><description>"Which chunks belong to document D?" — built lazily as chunks are seen.</description></item>
///   <item><description>"Which cache entries cited chunk X?" — built as the response and
///   retrieval caches register the chunks each entry was derived from.</description></item>
/// </list>
///
/// <para>
/// Composing the two gives the document → chunks → cache-entries path: a single
/// changed document resolves to every cache key that cited any of its chunks,
/// without scanning the cache (which <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// cannot enumerate anyway). The maps are in-process and lazily populated; a real
/// deployment would persist them or use Redis key tags, but the contract is the
/// same. The content-addressable embedding cache is deliberately <em>not</em>
/// tracked here — its SHA-keyed entries never go stale on a document change.
/// </para>
/// </summary>
public sealed class CachedDependencyTracker
{
    // chunkId -> the cache keys whose entries cited that chunk.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _keysByChunk =
        new(StringComparer.Ordinal);

    // documentId -> the chunk ids known to belong to that document.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _chunksByDocument =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Record that the cache entry <paramref name="cacheKey"/> was produced from
    /// the given <paramref name="citedChunkIds"/>. Called by the response and
    /// retrieval caches when they store an entry. The chunk → document link is
    /// derived from the stable <c>{DocumentId}#{ChunkIndex}</c> id form, so the
    /// document side of the index is populated as a side effect.
    /// </summary>
    /// <param name="cacheKey">The cache key of the stored entry.</param>
    /// <param name="citedChunkIds">The chunk ids the entry was derived from.</param>
    public void RegisterEntry(string cacheKey, IEnumerable<string> citedChunkIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(citedChunkIds);

        foreach (var chunkId in citedChunkIds)
        {
            if (string.IsNullOrWhiteSpace(chunkId))
            {
                continue;
            }

            var keys = _keysByChunk.GetOrAdd(chunkId, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            keys[cacheKey] = 0;

            // Derive the owning document from the stable {DocumentId}#{ChunkIndex}
            // id so "which chunks belong to D" stays in sync without a second call.
            var documentId = DocumentIdOf(chunkId);
            if (documentId is not null)
            {
                var chunks = _chunksByDocument.GetOrAdd(documentId, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
                chunks[chunkId] = 0;
            }
        }
    }

    /// <summary>
    /// Explicitly associate <paramref name="chunkIds"/> with
    /// <paramref name="documentId"/>. Useful when the ingest pipeline knows a
    /// document's chunk set independently of any cache entry (e.g. on re-ingest).
    /// </summary>
    public void RegisterDocumentChunks(string documentId, IEnumerable<string> chunkIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(chunkIds);

        var chunks = _chunksByDocument.GetOrAdd(documentId, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        foreach (var chunkId in chunkIds)
        {
            if (!string.IsNullOrWhiteSpace(chunkId))
            {
                chunks[chunkId] = 0;
            }
        }
    }

    /// <summary>The chunk ids currently known to belong to <paramref name="documentId"/>.</summary>
    public IReadOnlyCollection<string> ChunksForDocument(string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        return _chunksByDocument.TryGetValue(documentId, out var chunks)
            ? chunks.Keys.ToArray()
            : [];
    }

    /// <summary>
    /// The cache keys that cited any of the given <paramref name="chunkIds"/>,
    /// de-duplicated across chunks (one entry can cite several chunks of the same
    /// document). Returned keys are removed from the index — invalidation consumes
    /// them, so a second change to the same document does not re-evict the same
    /// (now-absent) keys.
    /// </summary>
    public IReadOnlyCollection<string> DrainKeysForChunks(IEnumerable<string> chunkIds)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunkId in chunkIds)
        {
            if (_keysByChunk.TryRemove(chunkId, out var cited))
            {
                foreach (var key in cited.Keys)
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    private static string? DocumentIdOf(string chunkId)
    {
        int hash = chunkId.IndexOf('#', StringComparison.Ordinal);
        return hash > 0 ? chunkId[..hash] : null;
    }
}
