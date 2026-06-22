namespace SmartDocs.Performance;

/// <summary>
/// The two freshness numbers the chapter names (Ch 22), plus the sample count
/// they were computed from.
/// </summary>
/// <param name="IndexLagP95">
/// 95th-percentile delay between a document being modified and its new vectors
/// becoming searchable (upserted-time − modified-at).
/// </param>
/// <param name="CacheLagP95">
/// 95th-percentile delay between a document being modified and the stale cached
/// answers derived from it being evicted (cache-evicted-time − modified-at).
/// </param>
/// <param name="Samples">How many observations the percentiles were computed from.</param>
public readonly record struct FreshnessReport(
    TimeSpan IndexLagP95,
    TimeSpan CacheLagP95,
    int Samples);

/// <summary>
/// Records the two staleness windows a production RAG system must keep small
/// (Ch 22): <em>index lag</em> — how long after a document changes its new
/// vectors become searchable — and <em>cache lag</em> — how long after a change
/// the stale cached answers derived from it are evicted. Each
/// <see cref="Record"/> call accumulates one observation; <see cref="Report"/>
/// returns the p95 of each window.
///
/// <para>
/// The percentile is computed with the deterministic nearest-rank method, so the
/// same observations always yield the same report (no sampling, no seed needed —
/// the calculation itself is exact). Cache lag is optional per observation: a
/// document whose change produced no cached entries to evict contributes only to
/// the index-lag window.
/// </para>
/// </summary>
public sealed class FreshnessEvaluator
{
    private readonly List<double> _indexLagSeconds = [];
    private readonly List<double> _cacheLagSeconds = [];

    /// <summary>The number of index-lag observations recorded so far.</summary>
    public int Samples => _indexLagSeconds.Count;

    /// <summary>
    /// Record one freshness observation for a document change.
    /// </summary>
    /// <param name="modifiedAt">When the document was modified at source.</param>
    /// <param name="indexedAt">When the document's new vectors became searchable.</param>
    /// <param name="cacheEvictedAt">
    /// When the stale cached answers derived from the document were evicted, or
    /// <see langword="null"/> if the change produced nothing to evict.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="indexedAt"/> (or <paramref name="cacheEvictedAt"/>)
    /// precedes <paramref name="modifiedAt"/> — a negative lag is never valid.
    /// </exception>
    public void Record(DateTimeOffset modifiedAt, DateTimeOffset indexedAt, DateTimeOffset? cacheEvictedAt = null)
    {
        var indexLag = indexedAt - modifiedAt;
        if (indexLag < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(indexedAt), "indexedAt must be at or after modifiedAt.");
        }

        _indexLagSeconds.Add(indexLag.TotalSeconds);

        if (cacheEvictedAt is { } evicted)
        {
            var cacheLag = evicted - modifiedAt;
            if (cacheLag < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(cacheEvictedAt), "cacheEvictedAt must be at or after modifiedAt.");
            }

            _cacheLagSeconds.Add(cacheLag.TotalSeconds);
        }
    }

    /// <summary>
    /// Compute the current freshness report (p95 index lag, p95 cache lag, sample
    /// count). With no observations both windows are <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public FreshnessReport Report() => new(
        IndexLagP95: Percentile95(_indexLagSeconds),
        CacheLagP95: Percentile95(_cacheLagSeconds),
        Samples: _indexLagSeconds.Count);

    /// <summary>
    /// Deterministic nearest-rank 95th percentile over a list of second-valued
    /// lags. Rank = ceil(0.95 · N), 1-based; an empty list is zero.
    /// </summary>
    private static TimeSpan Percentile95(List<double> seconds)
    {
        if (seconds.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var sorted = seconds.OrderBy(static s => s).ToArray();
        // Nearest-rank: index of the smallest value with at least 95% of the data
        // at or below it. Clamp to the last element for tiny samples.
        int rank = (int)Math.Ceiling(0.95 * sorted.Length);
        int index = Math.Clamp(rank - 1, 0, sorted.Length - 1);
        return TimeSpan.FromSeconds(sorted[index]);
    }
}
