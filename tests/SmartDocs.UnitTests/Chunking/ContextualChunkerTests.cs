using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Chunking;

namespace SmartDocs.UnitTests.Chunking;

public sealed class ContextualChunkerTests
{
    [Fact]
    public async Task Prepends_LLM_context_to_each_inner_chunk()
    {
        var inner = new FixedSizeChunker(chunkSize: 30, overlap: 0);
        var chat = new StubChatClient(_ => "This chunk discusses the Q3 revenue trend.");

        var contextual = new ContextualChunker(inner, chat);

        var meta = new DocumentMetadata("d1", "financial-reports", "Finance",
            "Montreal", "Restricted", "Report", 2026, "x",
            new DateOnly(2026, 1, 1), "Q3 Report");
        var doc = new Document(meta,
            "Q3 revenue was up 12%. Engineering hiring stayed on plan. Pipeline coverage healthy.",
            "data/test/q3.md");

        var chunks = new List<DocumentChunk>();
        await foreach (var c in contextual.ChunkAsync(doc))
        {
            chunks.Add(c);
        }

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.StartsWith("This chunk discusses", c.Text, StringComparison.Ordinal));
        // Original text is preserved underneath the context line.
        Assert.All(chunks, c => Assert.Contains(doc.Content[c.StartCharOffset..c.EndCharOffset].Trim()[..10], c.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void Strategy_name_carries_inner_strategy()
    {
        var inner = new FixedSizeChunker(50, 0);
        var chat = new StubChatClient(_ => "ctx");
        var contextual = new ContextualChunker(inner, chat);
        Assert.Equal("contextual(fixed-size)", contextual.Strategy);
    }
}
