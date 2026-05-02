using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Indexing;

namespace SmartDocs.UnitTests.Indexing;

public sealed class IndexingStrategyTests
{
    private static DocumentChunk Chunk(string text)
    {
        var meta = new DocumentMetadata("d", "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk("d#0", "d", 0, text, 0, text.Length, meta);
    }

    [Fact]
    public async Task ChunkIndexingStrategy_emits_chunk_text_unchanged()
    {
        var chunk = Chunk("Hello world. Bye now.");
        var items = await ToListAsync(new ChunkIndexingStrategy().IndexAsync(chunk));

        Assert.Single(items);
        Assert.Equal("Hello world. Bye now.", items[0].EmbedText);
        Assert.Same(chunk, items[0].Payload);
    }

    [Fact]
    public async Task SubChunkIndexingStrategy_emits_first_sentence()
    {
        var chunk = Chunk("First sentence. Second sentence. Third sentence.");
        var items = await ToListAsync(new SubChunkIndexingStrategy().IndexAsync(chunk));

        Assert.Single(items);
        Assert.Equal("First sentence.", items[0].EmbedText);
        Assert.Same(chunk, items[0].Payload);
    }

    [Fact]
    public async Task QueryIndexingStrategy_emits_one_item_per_LLM_line()
    {
        var chunk = Chunk("Employees receive 20 vacation days per fiscal year.");
        var chat = new StubChatClient(_ => "How many vacation days?\nWhen does the vacation year reset?\nWho approves leave?");
        var items = await ToListAsync(new QueryIndexingStrategy(chat, 3).IndexAsync(chunk));

        Assert.Equal(3, items.Count);
        Assert.Equal("How many vacation days?", items[0].EmbedText);
        Assert.Same(chunk, items[0].Payload);
    }

    [Fact]
    public async Task SummaryIndexingStrategy_emits_one_item_with_summary()
    {
        var chunk = Chunk("Long passage about HR policy details and exceptions.");
        var chat = new StubChatClient(_ => "HR policy with detailed exceptions.");
        var items = await ToListAsync(new SummaryIndexingStrategy(chat).IndexAsync(chunk));

        Assert.Single(items);
        Assert.Equal("HR policy with detailed exceptions.", items[0].EmbedText);
    }

    [Fact]
    public async Task IndexingPipelineBuilder_routes_by_extension()
    {
        var pipeline = new IndexingPipelineBuilder()
            .ForDocumentType(".md", new SubChunkIndexingStrategy())
            .Default(new ChunkIndexingStrategy())
            .Build();

        var chunk = Chunk("First. Second. Third.");

        var mdItems = await ToListAsync(pipeline.IndexAsync(chunk, "doc.md"));
        var pdfItems = await ToListAsync(pipeline.IndexAsync(chunk, "doc.pdf"));

        Assert.Equal("First.", mdItems[0].EmbedText);
        Assert.Equal("First. Second. Third.", pdfItems[0].EmbedText);
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> src)
    {
        var list = new List<T>();
        await foreach (var x in src)
        {
            list.Add(x);
        }

        return list;
    }
}
