using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Chunking;
using SmartDocs.Ingestion.Multimodal;

namespace SmartDocs.UnitTests.Multimodal;

public sealed class MultimodalChunkerTests
{
    [Fact]
    public async Task Inlines_image_caption_and_table_markdown_into_chunk_text()
    {
        var meta = new DocumentMetadata("fin-001", "financial-reports", "Finance",
            "Montreal", "Restricted", "Report", 2026, "x",
            new DateOnly(2026, 1, 1), "Q3 Report");
        var doc = new Document(meta, "Q3 revenue grew 12% quarter over quarter.", "data/test/q3.md");

        var images = new List<ExtractedImage>
        {
            new("fin-001#img-0", PageNumber: 1, Caption: "Bar chart showing 12% Q3 growth.", ImagePath: null),
        };
        var tables = new List<ExtractedTable>
        {
            new("fin-001#tbl-0", PageNumber: 1, Markdown: "| Quarter | Revenue |\n|---------|---------|\n| Q3 | 14M |"),
        };

        var chunker = new MultimodalChunker(new FixedSizeChunker(200, 0), images, tables);
        var chunks = new List<MultimodalChunk>();
        await foreach (var c in chunker.ChunkAsync(doc))
        {
            chunks.Add(c);
        }

        Assert.NotEmpty(chunks);
        var first = chunks[0];
        Assert.Contains("Bar chart showing 12% Q3 growth.", first.Chunk.Text, StringComparison.Ordinal);
        Assert.Contains("| Quarter | Revenue |", first.Chunk.Text, StringComparison.Ordinal);
        Assert.Single(first.Images);
        Assert.Single(first.Tables);
    }

    [Fact]
    public async Task Skips_caption_inline_when_caption_is_empty()
    {
        var meta = new DocumentMetadata("d", "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "Title");
        var doc = new Document(meta, "Some body text.", "data/test/d.md");
        var images = new List<ExtractedImage> { new("d#img-0", 1, Caption: "", ImagePath: null) };

        var chunker = new MultimodalChunker(new FixedSizeChunker(200, 0), images, []);
        var chunks = new List<MultimodalChunk>();
        await foreach (var c in chunker.ChunkAsync(doc))
        {
            chunks.Add(c);
        }

        Assert.DoesNotContain("[Figure", chunks[0].Chunk.Text, StringComparison.Ordinal);
    }
}
