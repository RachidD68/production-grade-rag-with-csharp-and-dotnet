using SmartDocs.Core.Documents;
using SmartDocs.Evaluation;

namespace SmartDocs.UnitTests.Evaluation;

public sealed class EvaluationTests
{
    private static DocumentChunk Chunk(string id, string text = "x")
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk(id + "#0", id, 0, text, 0, text.Length, meta);
    }

    [Fact]
    public void RetrievalEvaluator_computes_recall_precision_mrr_ndcg()
    {
        var gold = new GoldenItem("vacation?", new HashSet<string>(["a", "b"]));
        var hits = new[]
        {
            new RetrievalResult(Chunk("a"), 0.9),
            new RetrievalResult(Chunk("c"), 0.8),
            new RetrievalResult(Chunk("b"), 0.7),
        };
        var metrics = RetrievalEvaluator.Evaluate([(gold, hits)], k: 3);

        Assert.Equal(1.0, metrics.RecallAtK, precision: 4);
        Assert.Equal(2.0 / 3, metrics.PrecisionAtK, precision: 4);
        Assert.Equal(1.0, metrics.Mrr, precision: 4);
        Assert.True(metrics.NdcgAtK > 0 && metrics.NdcgAtK <= 1.0);
    }

    [Fact]
    public async Task GenerationEvaluator_aggregates_per_sentence_verdicts()
    {
        var judge = new StubChatClient(p =>
        {
            // Inspect only the substring after the "Claim:" marker so the source-context
            // mention of "vacation" doesn't leak into the verdict.
            var idx = p.IndexOf("Claim:", StringComparison.Ordinal);
            var claim = idx < 0 ? p : p[idx..];
            if (claim.Contains("Vacation", StringComparison.OrdinalIgnoreCase))
            {
                return "SUPPORTED";
            }
            if (claim.Contains("Bonus", StringComparison.OrdinalIgnoreCase))
            {
                return "NOT_SUPPORTED";
            }
            return "PARTIALLY_SUPPORTED";
        });
        var evaluator = new GenerationEvaluator(judge);

        var sources = new[]
        {
            new RetrievalResult(Chunk("a", "20 paid vacation days per fiscal year"), 0.9),
        };
        var assessment = await evaluator.AssessAsync(
            "vacation?",
            "Vacation days are paid here. Bonus is unsupported. Other things happen too.",
            sources);

        Assert.Equal(1, assessment.SupportedClaims);
        Assert.Equal(1, assessment.NotSupportedClaims);
        Assert.Equal(1, assessment.PartiallySupportedClaims);
        // (1 SUPPORTED + 0.5 * 1 PARTIAL) / 3 = 0.5
        Assert.Equal(0.5, assessment.FaithfulnessScore, precision: 3);
    }
}
