using SmartDocs.Performance;

namespace SmartDocs.UnitTests.Performance;

public sealed class FreshnessEvaluatorTests
{
    private static readonly DateTimeOffset Base = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Report_computes_p95_index_and_cache_lag_deterministically()
    {
        var eval = new FreshnessEvaluator();

        // 20 observations: index lag 1..20s, cache lag 2..40s (even multiples).
        for (int i = 1; i <= 20; i++)
        {
            eval.Record(
                modifiedAt: Base,
                indexedAt: Base.AddSeconds(i),
                cacheEvictedAt: Base.AddSeconds(2 * i));
        }

        var report = eval.Report();

        // Nearest-rank p95 over 20 samples → rank ceil(0.95*20)=19 → 19th smallest.
        Assert.Equal(20, report.Samples);
        Assert.Equal(TimeSpan.FromSeconds(19), report.IndexLagP95);
        Assert.Equal(TimeSpan.FromSeconds(38), report.CacheLagP95);
    }

    [Fact]
    public void Report_with_no_samples_is_zero()
    {
        var report = new FreshnessEvaluator().Report();
        Assert.Equal(0, report.Samples);
        Assert.Equal(TimeSpan.Zero, report.IndexLagP95);
        Assert.Equal(TimeSpan.Zero, report.CacheLagP95);
    }

    [Fact]
    public void Record_without_cache_eviction_omits_the_cache_window()
    {
        var eval = new FreshnessEvaluator();
        eval.Record(Base, Base.AddSeconds(5)); // no cacheEvictedAt
        var report = eval.Report();

        Assert.Equal(1, report.Samples);
        Assert.Equal(TimeSpan.FromSeconds(5), report.IndexLagP95);
        Assert.Equal(TimeSpan.Zero, report.CacheLagP95); // no cache observations
    }

    [Fact]
    public void Record_rejects_negative_index_lag()
    {
        var eval = new FreshnessEvaluator();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            eval.Record(Base, Base.AddSeconds(-1)));
    }
}
