using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Retrieval.Hybrid;

namespace SmartDocs.UnitTests.Retrieval;

public sealed class HybridFusionTests
{
    private static DocumentChunk Chunk(string id)
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk(id + "#0", id, 0, "x", 0, 1, meta);
    }

    [Fact]
    public void Weighted_fusion_blends_normalised_scores()
    {
        var a = new[] {
            new RetrievalResult(Chunk("alpha"), 1.0),
            new RetrievalResult(Chunk("beta"),  0.0),
        };
        var b = new[] {
            new RetrievalResult(Chunk("beta"),  10.0),
            new RetrievalResult(Chunk("gamma"), 5.0),
        };

        var fusion = new FusionService(FusionStrategy.Weighted, weightA: 0.5, weightB: 0.5);
        var fused = fusion.Fuse(a, b, topK: 3);

        Assert.Equal(3, fused.Count);
        // beta is normalised top in list b and bottom in list a -> mid-pack but present.
        Assert.Contains(fused, r => r.Chunk.DocumentId == "beta");
    }

    [Fact]
    public void Cascade_returns_first_list_when_floor_met()
    {
        var a = new[] {
            new RetrievalResult(Chunk("a1"), 1.0),
            new RetrievalResult(Chunk("a2"), 0.9),
            new RetrievalResult(Chunk("a3"), 0.8),
        };
        var b = new[] { new RetrievalResult(Chunk("bx"), 0.5) };

        var fusion = new FusionService(FusionStrategy.Cascade, cascadeFloorCount: 3);
        var fused = fusion.Fuse(a, b, topK: 5);

        Assert.Equal(3, fused.Count);
        Assert.DoesNotContain(fused, r => r.Chunk.DocumentId == "bx");
    }

    [Fact]
    public void Cascade_falls_through_to_RRF_when_first_list_too_small()
    {
        var a = new[] { new RetrievalResult(Chunk("a1"), 1.0) };
        var b = new[] {
            new RetrievalResult(Chunk("b1"), 5.0),
            new RetrievalResult(Chunk("b2"), 4.0),
        };

        var fusion = new FusionService(FusionStrategy.Cascade, cascadeFloorCount: 3);
        var fused = fusion.Fuse(a, b, topK: 5);

        Assert.Equal(3, fused.Count);
    }

    [Fact]
    public async Task HybridDatabaseRetriever_runs_both_legs_and_fuses()
    {
        var vector = new ConstantRetriever([
            new RetrievalResult(Chunk("v1"), 0.9),
            new RetrievalResult(Chunk("shared"), 0.7),
        ]);
        var graph = new ConstantRetriever([
            new RetrievalResult(Chunk("shared"), 0.8),
            new RetrievalResult(Chunk("g1"), 0.5),
        ]);
        var hybrid = new HybridDatabaseRetriever(vector, graph);

        var results = await hybrid.RetrieveAsync("q", topK: 3);

        Assert.Equal(3, results.Count);
        // 'shared' appears in both lists -> highest fused score under RRF.
        Assert.Equal("shared", results[0].Chunk.DocumentId);
    }

    private sealed class ConstantRetriever : IRetriever
    {
        private readonly IReadOnlyList<RetrievalResult> _hits;
        public ConstantRetriever(IReadOnlyList<RetrievalResult> hits) { _hits = hits; }
        public string Strategy => "stub";
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string q, int topK, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([.. _hits.Take(topK)]);
    }
}
