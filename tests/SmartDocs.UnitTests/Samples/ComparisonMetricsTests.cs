using RagInDotNet.Samples.Ch06_VectorDbComparison;

namespace SmartDocs.UnitTests.Samples;

/// <summary>
/// Unit tests for the Ch06 comparison sample's recall@k and latency-percentile
/// helpers (<see cref="ComparisonMetrics"/>).
/// </summary>
public sealed class ComparisonMetricsTests
{
    [Fact]
    public void RecallAtK_perfect_when_all_relevant_retrieved()
    {
        string[] relevant = ["a", "b", "c", "d"];
        string[] retrieved = ["a", "b", "c", "d", "e", "f"];

        Assert.Equal(1.0, ComparisonMetrics.RecallAtK(retrieved, relevant));
    }

    [Fact]
    public void RecallAtK_is_fraction_of_relevant_found()
    {
        string[] relevant = ["a", "b", "c", "d"];
        string[] retrieved = ["a", "b", "x", "y"]; // 2 of 4 relevant

        Assert.Equal(0.5, ComparisonMetrics.RecallAtK(retrieved, relevant));
    }

    [Fact]
    public void RecallAtK_ignores_duplicate_retrievals()
    {
        string[] relevant = ["a", "b"];
        string[] retrieved = ["a", "a", "a"]; // 1 unique relevant => 0.5

        Assert.Equal(0.5, ComparisonMetrics.RecallAtK(retrieved, relevant));
    }

    [Fact]
    public void RecallAtK_zero_for_empty_gold_set()
    {
        Assert.Equal(0.0, ComparisonMetrics.RecallAtK(["a"], []));
    }

    [Fact]
    public void Percentile_p50_is_median()
    {
        double[] samples = [1, 2, 3, 4, 5];
        Assert.Equal(3.0, ComparisonMetrics.Percentile(samples, 50));
    }

    [Fact]
    public void Percentile_handles_unsorted_input()
    {
        double[] samples = [5, 1, 3, 2, 4];
        Assert.Equal(3.0, ComparisonMetrics.Percentile(samples, 50));
    }

    [Fact]
    public void Percentile_interpolates_between_ranks()
    {
        // p95 of [0..100 step 10] => rank = 0.95 * 10 = 9.5 => between 90 and 100.
        double[] samples = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100];
        Assert.Equal(95.0, ComparisonMetrics.Percentile(samples, 95), precision: 6);
    }

    [Fact]
    public void Percentile_single_sample_returns_that_sample()
    {
        Assert.Equal(42.0, ComparisonMetrics.Percentile([42.0], 95));
    }

    [Fact]
    public void Percentile_throws_on_empty()
    {
        Assert.Throws<ArgumentException>(() => ComparisonMetrics.Percentile([], 50));
    }

    [Fact]
    public void Mean_of_recalls()
    {
        Assert.Equal(0.5, ComparisonMetrics.Mean([0.25, 0.75]));
        Assert.Equal(0.0, ComparisonMetrics.Mean([]));
    }
}
