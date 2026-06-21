using SmartDocs.Core.Documents;
using SmartDocs.Retrieval;

namespace SmartDocs.UnitTests.Retrieval;

public sealed class ContextOrderingTests
{
    private static readonly string[] ExpectedOrder = ["r1", "r3", "r5", "r4", "r2"];

    private static RetrievalResult Result(string id, double score)
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        var chunk = new DocumentChunk(id + "#0", id, 0, id, 0, id.Length, meta);
        return new RetrievalResult(chunk, score);
    }

    [Fact]
    public void Interleave_puts_rank1_first_and_rank2_last_and_preserves_count()
    {
        // Relevance order: r1 (best) … r5 (worst).
        var ranked = new[]
        {
            Result("r1", 0.99),
            Result("r2", 0.80),
            Result("r3", 0.60),
            Result("r4", 0.40),
            Result("r5", 0.20),
        };

        var ordered = ContextOrdering.Interleave(ranked);

        // [1,2,3,4,5] -> [1,3,5,4,2]: strongest at the ends, weakest in the middle.
        Assert.Equal(5, ordered.Count);
        Assert.Equal("r1", ordered[0].Chunk.DocumentId);   // best at the START
        Assert.Equal("r2", ordered[^1].Chunk.DocumentId);  // 2nd-best at the END
        Assert.Equal(ExpectedOrder, ordered.Select(o => o.Chunk.DocumentId).ToArray());

        // Round-trip: same set of items, just reordered.
        Assert.Equal(
            ranked.Select(r => r.Chunk.DocumentId).OrderBy(x => x, StringComparer.Ordinal),
            ordered.Select(o => o.Chunk.DocumentId).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Interleave_empty_returns_empty()
    {
        var ordered = ContextOrdering.Interleave(Array.Empty<RetrievalResult>());
        Assert.Empty(ordered);
    }

    [Fact]
    public void Interleave_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ContextOrdering.Interleave(null!));
    }
}
