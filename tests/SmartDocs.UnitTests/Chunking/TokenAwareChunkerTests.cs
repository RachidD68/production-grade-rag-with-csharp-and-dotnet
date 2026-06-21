using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;
using SmartDocs.Ingestion.Chunking;

namespace SmartDocs.UnitTests.Chunking;

public sealed class TokenAwareChunkerTests
{
    private static DocumentMetadata Meta() => new(
        "doc-001", "hr-policies", "HR", "Montreal", "Internal", "Policy",
        2026, "A", new DateOnly(2026, 1, 1), "Test");

    private static Document Doc(string content) =>
        new(Meta(), content, "data/test/doc-001.md");

    [Fact]
    public async Task Enforces_token_budget_when_inner_chunk_is_too_large()
    {
        var tokenCounter = new TokenCounter();
        // A single fixed-size chunk far larger than the token budget.
        var inner = new FixedSizeChunker(chunkSize: 4000, overlap: 0);
        var sut = new TokenAwareChunker(inner, tokenCounter, maxTokens: 16);

        var words = string.Join(' ', Enumerable.Range(0, 400).Select(i => $"word{i}"));
        var chunks = await ToListAsync(sut.ChunkAsync(Doc(words)));

        Assert.True(chunks.Count > 1, "the oversized chunk should be sub-split");
        Assert.All(chunks, c => Assert.True(
            tokenCounter.CountTokens(c.Text) <= 16,
            $"chunk over budget ({tokenCounter.CountTokens(c.Text)} tokens): {c.Text}"));
    }

    [Fact]
    public async Task Passes_chunks_within_budget_through_unchanged_and_reindexes()
    {
        var tokenCounter = new TokenCounter();
        var inner = new SentenceChunker(maxSentencesPerChunk: 1);
        var sut = new TokenAwareChunker(inner, tokenCounter, maxTokens: 512);

        var chunks = await ToListAsync(sut.ChunkAsync(Doc(
            "First sentence here. Second sentence here. Third sentence here.")));

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.True(tokenCounter.CountTokens(c.Text) <= 512));
        // ChunkIndex stays contiguous from zero after the decorator re-indexes.
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
            Assert.Equal($"{chunks[i].DocumentId}#{i}", chunks[i].ChunkId);
        }
    }

    [Fact]
    public void Strategy_name_wraps_inner_strategy()
    {
        var sut = new TokenAwareChunker(new RecursiveCharacterChunker(800), new TokenCounter(), maxTokens: 256);
        Assert.Equal("token-aware(recursive)", sut.Strategy);
    }

    [Fact]
    public async Task No_content_is_lost_across_the_sub_split()
    {
        var tokenCounter = new TokenCounter();
        var inner = new FixedSizeChunker(chunkSize: 4000, overlap: 0);
        var sut = new TokenAwareChunker(inner, tokenCounter, maxTokens: 8);

        var words = Enumerable.Range(0, 120).Select(i => $"token{i}").ToArray();
        var chunks = await ToListAsync(sut.ChunkAsync(Doc(string.Join(' ', words))));

        var rejoined = string.Join(' ', chunks.Select(c => c.Text));
        foreach (var w in words)
        {
            Assert.Contains(w, rejoined, StringComparison.Ordinal);
        }
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var x in source)
        {
            list.Add(x);
        }

        return list;
    }
}
