using SmartDocs.Evaluation;

namespace SmartDocs.UnitTests.Evaluation;

public sealed class StatisticalSignificanceTests
{
    // --- Wilson interval -----------------------------------------------------

    [Fact]
    public void WilsonInterval_matches_known_bounds_for_8_of_10()
    {
        // Reference values computed from the Wilson score formula (z = 1.96).
        var ci = StatisticalSignificance.WilsonInterval(successes: 8, total: 10);

        Assert.Equal(0.49016, ci.Lower, precision: 4);
        Assert.Equal(0.94332, ci.Upper, precision: 4);
        Assert.False(ci.ContainsZero);
    }

    [Fact]
    public void WilsonInterval_matches_known_bounds_for_50_of_100()
    {
        var ci = StatisticalSignificance.WilsonInterval(successes: 50, total: 100);

        Assert.Equal(0.40383, ci.Lower, precision: 4);
        Assert.Equal(0.59617, ci.Upper, precision: 4);
    }

    [Fact]
    public void WilsonInterval_stays_inside_unit_range_at_the_extremes()
    {
        var none = StatisticalSignificance.WilsonInterval(successes: 0, total: 10);
        var all = StatisticalSignificance.WilsonInterval(successes: 10, total: 10);

        Assert.Equal(0.0, none.Lower, precision: 6);
        Assert.True(none.Upper is > 0 and < 1);
        Assert.True(all.Lower is > 0 and < 1);
        Assert.Equal(1.0, all.Upper, precision: 6);
    }

    [Fact]
    public void WilsonInterval_rejects_more_successes_than_trials()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StatisticalSignificance.WilsonInterval(successes: 11, total: 10));
    }

    [Fact]
    public void WilsonInterval_rejects_zero_total()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StatisticalSignificance.WilsonInterval(successes: 0, total: 0));
    }

    // --- Paired bootstrap delta ----------------------------------------------

    [Fact]
    public void PairedBootstrapDelta_excludes_zero_for_a_consistent_improvement()
    {
        // Every candidate item scores 0.2 higher than its baseline → the per-item
        // delta is a constant 0.2, so every bootstrap resample yields 0.2 and the
        // CI collapses to a point that excludes zero.
        var baseline = Enumerable.Repeat(0.6, 30).ToList();
        var candidate = Enumerable.Repeat(0.8, 30).ToList();

        var verdict = StatisticalSignificance.PairedBootstrapDelta(baseline, candidate, seed: 42);

        Assert.Equal(0.2, verdict.Delta, precision: 6);
        Assert.True(verdict.IsSignificant);
        Assert.False(verdict.Interval.ContainsZero);
        Assert.False(verdict.IsSignificantRegression); // improvement, not regression
    }

    [Fact]
    public void PairedBootstrapDelta_straddles_zero_for_noise()
    {
        // Differences are symmetric around zero (half +0.1, half -0.1), so the
        // mean delta is ~0 and its CI straddles zero — not significant.
        var rng = new Random(7);
        var baseline = new List<double>();
        var candidate = new List<double>();
        for (int i = 0; i < 40; i++)
        {
            double b = rng.NextDouble();
            baseline.Add(b);
            candidate.Add(i % 2 == 0 ? b + 0.1 : b - 0.1);
        }

        var verdict = StatisticalSignificance.PairedBootstrapDelta(baseline, candidate, seed: 123);

        Assert.True(verdict.Interval.ContainsZero);
        Assert.False(verdict.IsSignificant);
    }

    [Fact]
    public void PairedBootstrapDelta_flags_a_significant_regression()
    {
        var baseline = Enumerable.Repeat(0.9, 25).ToList();
        var candidate = Enumerable.Repeat(0.6, 25).ToList();

        var verdict = StatisticalSignificance.PairedBootstrapDelta(baseline, candidate, seed: 99);

        Assert.True(verdict.Delta < 0);
        Assert.True(verdict.IsSignificant);
        Assert.True(verdict.IsSignificantRegression);
    }

    [Fact]
    public void PairedBootstrapDelta_is_reproducible_for_a_fixed_seed()
    {
        var rng = new Random(11);
        var baseline = Enumerable.Range(0, 30).Select(_ => rng.NextDouble()).ToList();
        var candidate = baseline.Select(b => b + 0.05).ToList();

        var first = StatisticalSignificance.PairedBootstrapDelta(baseline, candidate, seed: 2026);
        var second = StatisticalSignificance.PairedBootstrapDelta(baseline, candidate, seed: 2026);

        Assert.Equal(first.Interval.Lower, second.Interval.Lower, precision: 12);
        Assert.Equal(first.Interval.Upper, second.Interval.Upper, precision: 12);
    }

    [Fact]
    public void PairedBootstrapDelta_rejects_mismatched_lengths()
    {
        Assert.Throws<ArgumentException>(
            () => StatisticalSignificance.PairedBootstrapDelta([0.1, 0.2], [0.1], seed: 1));
    }

    // --- BootstrapMean -------------------------------------------------------

    [Fact]
    public void BootstrapMean_brackets_the_sample_mean()
    {
        var scores = new[] { 0.7, 0.8, 0.9, 0.6, 0.85, 0.75, 0.95, 0.65 };
        double mean = scores.Average();

        var ci = StatisticalSignificance.BootstrapMean(scores, seed: 5);

        Assert.True(ci.Lower <= mean && mean <= ci.Upper);
        Assert.True(ci.Width > 0);
    }

    // --- McNemar -------------------------------------------------------------

    [Fact]
    public void McNemar_counts_discordant_pairs_and_is_significant_for_a_one_sided_flip()
    {
        // 10 items the baseline passed now fail; nothing flips the other way.
        // That lopsided flip pattern is significant.
        var baseline = Enumerable.Repeat(true, 12).ToList();
        var candidate = Enumerable.Repeat(true, 12).ToList();
        for (int i = 0; i < 10; i++)
        {
            candidate[i] = false; // baseline pass, candidate fail
        }

        var result = StatisticalSignificance.McNemar(baseline, candidate);

        Assert.Equal(10, result.BaselinePassCandidateFail);
        Assert.Equal(0, result.BaselineFailCandidatePass);
        Assert.Equal(10, result.DiscordantPairs);
        Assert.True(result.IsSignificant());
    }

    [Fact]
    public void McNemar_is_not_significant_for_a_balanced_flip()
    {
        // Two flips each way — symmetric, so no evidence of a real change.
        var baseline = new[] { true, true, false, false, true, true, false, false };
        var candidate = new[] { false, true, true, false, false, true, true, false };

        var result = StatisticalSignificance.McNemar(baseline, candidate);

        Assert.False(result.IsSignificant());
    }

    [Fact]
    public void McNemar_handles_no_flips_at_all()
    {
        var same = new[] { true, false, true, false };

        var result = StatisticalSignificance.McNemar(same, same);

        Assert.Equal(0, result.DiscordantPairs);
        Assert.Equal(1.0, result.PValue, precision: 6);
        Assert.False(result.IsSignificant());
    }
}
