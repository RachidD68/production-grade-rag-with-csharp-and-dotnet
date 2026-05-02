using SmartDocs.Core.Documents;
using SmartDocs.Generation.Citations;

namespace SmartDocs.UnitTests.Generation;

public sealed class CitationTests
{
    private static DocumentChunk Chunk(string id, string title)
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), title);
        return new DocumentChunk(id + "#0", id, 0, "x", 0, 1, meta);
    }

    [Fact]
    public void CitationPipeline_extracts_all_source_markers()
    {
        var sources = new[]
        {
            new RetrievalResult(Chunk("a", "Vacation Policy"), 0.9),
            new RetrievalResult(Chunk("b", "Sick Policy"), 0.8),
        };
        var answer = "Employees get 20 days [Source 1]. Sick leave is unlimited [Source 2].";

        var citations = CitationPipeline.ExtractCitations(answer, sources);

        Assert.Equal(2, citations.Count);
        Assert.Equal("a#0", citations[0].ChunkId);
        Assert.Equal("Vacation Policy", citations[0].Title);
        Assert.Equal("b#0", citations[1].ChunkId);
    }

    [Fact]
    public void CitationPipeline_skips_invalid_source_indices()
    {
        var sources = new[] { new RetrievalResult(Chunk("a", "Only Source"), 0.9) };

        var citations = CitationPipeline.ExtractCitations(
            "First [Source 1]. Second [Source 99]. Third [Source 0].", sources);

        Assert.Single(citations);
        Assert.Equal(1, citations[0].SourceIndex);
    }

    [Fact]
    public async Task AuditLogger_writes_a_jsonl_entry_to_the_sink()
    {
        AuditEntry? captured = null;
        var logger = new AuditLogger(entry => { captured = entry; return Task.CompletedTask; });

        var sources = new[] { new RetrievalResult(Chunk("a", "T"), 0.9) };
        await logger.LogAsync(
            query: "what?",
            retrievedSources: sources,
            response: "Answer [Source 1].",
            citations: [new Citation("Answer", 1, "a#0", "a", "T", "SUPPORTED")],
            faithfulnessScore: 0.95,
            userId: "alice@contoso.com");

        Assert.NotNull(captured);
        Assert.Equal("what?", captured.Query);
        Assert.Equal(0.95, captured.FaithfulnessScore);
        Assert.Equal("alice@contoso.com", captured.UserId);
        Assert.Equal("a#0", captured.RetrievedChunkIds[0]);
    }
}
