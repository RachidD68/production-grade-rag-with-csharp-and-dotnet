using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Retrieval;

namespace SmartDocs.UnitTests.Retrieval;

/// <summary>
/// Chapter 8 retriever-stage building blocks: MMR diversity selection,
/// the minimum-score abstention filter, and the dense/sparse/hybrid
/// mode factory.
/// </summary>
public sealed class Ch08RetrieverTests
{
    private static DocumentChunk Chunk(string id, string text = "x")
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk(id + "#0", id, 0, text, 0, text.Length, meta);
    }

    // Helper so vector literals are not passed inline as constant array arguments.
    private static ReadOnlyMemory<float> Vec(float x, float y) => new float[] { x, y };

    private sealed class ConstantRetriever : IRetriever
    {
        private readonly IReadOnlyList<RetrievalResult> _hits;
        private readonly string _strategy;
        public ConstantRetriever(IReadOnlyList<RetrievalResult> hits, string strategy = "stub")
        {
            _hits = hits;
            _strategy = strategy;
        }
        public string Strategy => _strategy;
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string q, int topK, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([.. _hits.Take(topK)]);
    }

    // ---- MmrSelector ----------------------------------------------------

    [Fact]
    public void Mmr_deprioritises_near_duplicate_candidates()
    {
        // a and a' are near-identical vectors; b is orthogonal but slightly less
        // relevant. With diversity weight, the second pick should be b, not a'.
        var a = new MmrCandidate(Chunk("a"), Vec(1f, 0f), Relevance: 1.00);
        var aDup = new MmrCandidate(Chunk("a-dup"), Vec(0.99f, 0.01f), Relevance: 0.98);
        var b = new MmrCandidate(Chunk("b"), Vec(0f, 1f), Relevance: 0.90);

        var selected = MmrSelector.Select([a, aDup, b], k: 2, lambda: 0.5);

        Assert.Equal(2, selected.Count);
        Assert.Equal("a", selected[0].Chunk.DocumentId);   // most relevant first
        Assert.Equal("b", selected[1].Chunk.DocumentId);   // diverse pick beats the near-duplicate
    }

    [Fact]
    public void Mmr_lambda_one_reduces_to_pure_relevance_order()
    {
        var a = new MmrCandidate(Chunk("a"), Vec(1f, 0f), Relevance: 1.00);
        var aDup = new MmrCandidate(Chunk("a-dup"), Vec(0.99f, 0.01f), Relevance: 0.98);
        var b = new MmrCandidate(Chunk("b"), Vec(0f, 1f), Relevance: 0.90);

        var selected = MmrSelector.Select([b, aDup, a], k: 3, lambda: 1.0);

        // Pure Relevance: a (1.00) > a-dup (0.98) > b (0.90), regardless of similarity.
        Assert.Equal(["a", "a-dup", "b"], selected.Select(r => r.Chunk.DocumentId));
        Assert.Equal(1.00, selected[0].Score, 3);
    }

    [Fact]
    public void Mmr_returns_at_most_k_and_handles_small_pools()
    {
        var only = new MmrCandidate(Chunk("solo"), Vec(1f, 0f), Relevance: 0.5);

        var selected = MmrSelector.Select([only], k: 10, lambda: 0.7);

        Assert.Single(selected);
        Assert.Equal("solo", selected[0].Chunk.DocumentId);
    }

    [Fact]
    public void Mmr_empty_pool_returns_empty()
    {
        var selected = MmrSelector.Select([], k: 5, lambda: 0.5);
        Assert.Empty(selected);
    }

    [Fact]
    public void Mmr_rejects_out_of_range_lambda()
    {
        var c = new MmrCandidate(Chunk("a"), Vec(1f, 0f), 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(() => MmrSelector.Select([c], k: 1, lambda: 1.5));
    }

    // ---- MinScoreFilter -------------------------------------------------

    [Fact]
    public async Task MinScoreFilter_drops_everything_below_floor()
    {
        var inner = new ConstantRetriever([
            new RetrievalResult(Chunk("a"), 0.10),
            new RetrievalResult(Chunk("b"), 0.20),
        ]);
        var filtered = new MinScoreFilter(inner, minScore: 0.5);

        var hits = await filtered.RetrieveAsync("q", topK: 10);

        Assert.Empty(hits); // nothing clears the floor -> abstention path
    }

    [Fact]
    public async Task MinScoreFilter_keeps_results_at_or_above_floor()
    {
        var inner = new ConstantRetriever([
            new RetrievalResult(Chunk("a"), 0.90),
            new RetrievalResult(Chunk("b"), 0.40),
            new RetrievalResult(Chunk("c"), 0.50),
        ]);
        var filtered = new MinScoreFilter(inner, minScore: 0.5);

        var hits = await filtered.RetrieveAsync("q", topK: 10);

        Assert.Equal(["a", "c"], hits.Select(h => h.Chunk.DocumentId));
    }

    [Fact]
    public async Task MinScoreFilter_default_floor_is_passthrough()
    {
        var inner = new ConstantRetriever([
            new RetrievalResult(Chunk("a"), 0.01),
            new RetrievalResult(Chunk("b"), -0.50),
        ]);
        var filtered = new MinScoreFilter(inner); // default minScore = 0.0

        var hits = await filtered.RetrieveAsync("q", topK: 10);

        Assert.Equal(2, hits.Count); // unchanged
        Assert.Equal(["a", "b"], hits.Select(h => h.Chunk.DocumentId));
    }

    [Fact]
    public void MinScoreFilter_strategy_wraps_inner_name()
    {
        var inner = new ConstantRetriever([], strategy: "dense");
        Assert.Equal("min-score(dense)", new MinScoreFilter(inner, 0.3).Strategy);
    }

    // ---- RetrieverModeFactory -------------------------------------------

    [Fact]
    public void Factory_dense_returns_the_dense_leg()
    {
        var dense = new ConstantRetriever([], "dense");
        var sparse = new ConstantRetriever([], "sparse-bm25");

        var retriever = RetrieverModeFactory.Create("dense", dense, sparse);

        Assert.Same(dense, retriever);
    }

    [Fact]
    public void Factory_sparse_returns_the_sparse_leg()
    {
        var dense = new ConstantRetriever([], "dense");
        var sparse = new ConstantRetriever([], "sparse-bm25");

        var retriever = RetrieverModeFactory.Create("sparse", dense, sparse);

        Assert.Same(sparse, retriever);
    }

    [Fact]
    public void Factory_hybrid_returns_a_hybrid_retriever()
    {
        var dense = new ConstantRetriever([], "dense");
        var sparse = new ConstantRetriever([], "sparse-bm25");

        var retriever = RetrieverModeFactory.Create("hybrid", dense, sparse);

        Assert.IsType<HybridRetriever>(retriever);
        Assert.Equal("hybrid(dense+sparse-bm25)", retriever.Strategy);
    }

    [Theory]
    [InlineData(" Hybrid ")]
    [InlineData("DENSE")]
    public void Factory_is_case_and_whitespace_insensitive(string mode)
    {
        var dense = new ConstantRetriever([], "dense");
        var sparse = new ConstantRetriever([], "sparse-bm25");

        var retriever = RetrieverModeFactory.Create(mode, dense, sparse);

        Assert.NotNull(retriever);
    }

    [Fact]
    public void Factory_rejects_unknown_mode()
    {
        var dense = new ConstantRetriever([], "dense");
        var sparse = new ConstantRetriever([], "sparse-bm25");

        Assert.Throws<ArgumentException>(() => RetrieverModeFactory.Create("graph", dense, sparse));
    }
}
