namespace RagInDotNet.Samples.Ch06_VectorDbComparison;

/// <summary>
/// Measurement helpers for the vector-DB comparison: recall@k against a gold
/// set and latency percentiles. Kept separate from <c>Program.cs</c> so the
/// arithmetic is unit-testable (see Chapter 6 §benchmarking methodology).
/// </summary>
public static class ComparisonMetrics
{
    /// <summary>
    /// recall@k = |retrieved ∩ relevant| / |relevant|. <paramref name="retrieved"/>
    /// is the store's top-k ids for a query; <paramref name="relevant"/> is the
    /// gold set for that query. Returns 0 when the gold set is empty.
    /// </summary>
    public static double RecallAtK(IEnumerable<string> retrieved, IReadOnlyCollection<string> relevant)
    {
        ArgumentNullException.ThrowIfNull(retrieved);
        ArgumentNullException.ThrowIfNull(relevant);
        if (relevant.Count == 0)
        {
            return 0d;
        }
        var relevantSet = relevant as HashSet<string> ?? new HashSet<string>(relevant, StringComparer.Ordinal);
        var hits = retrieved.Distinct(StringComparer.Ordinal).Count(relevantSet.Contains);
        return (double)hits / relevant.Count;
    }

    /// <summary>
    /// The <paramref name="percentile"/> (0–100) of <paramref name="samples"/>
    /// using linear interpolation between closest ranks. Latencies don't need
    /// to be pre-sorted. Throws on an empty sample set.
    /// </summary>
    public static double Percentile(IReadOnlyList<double> samples, double percentile)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("Cannot compute a percentile of an empty sample set.", nameof(samples));
        }
        if (percentile is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "Percentile must be in [0, 100].");
        }

        // Copy then in-place Array.Sort (introsort) — avoids the LINQ OrderBy
        // enumerator + stable-sort overhead in this measurement helper.
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        var rank = percentile / 100d * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return sorted[lower];
        }
        var weight = rank - lower;
        return sorted[lower] + (weight * (sorted[upper] - sorted[lower]));
    }

    /// <summary>Mean recall across a set of per-query recall values (0 when empty).</summary>
    public static double Mean(IReadOnlyCollection<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.Count == 0 ? 0d : values.Sum() / values.Count;
    }
}
