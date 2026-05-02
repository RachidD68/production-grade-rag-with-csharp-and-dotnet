using SmartDocs.Core.Documents;

namespace SmartDocs.UnitTests.Documents;

public sealed class DomainModelTests
{
    private static DocumentMetadata SampleMeta() => new(
        Id: "hr-001",
        Silo: "hr-policies",
        Department: "HR",
        Office: "Montreal",
        ConfidentialityLevel: "Internal",
        DocumentType: "Policy",
        FiscalYear: 2026,
        Author: "Amélie Tremblay",
        LastModified: new DateOnly(2026, 4, 5),
        Title: "Annual Leave Policy (Montreal, FY2026)");

    [Fact]
    public void DocumentMetadata_value_equality_holds()
    {
        var a = SampleMeta();
        var b = SampleMeta();

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DocumentMetadata_with_changes_metadata_via_record_with_expression()
    {
        var a = SampleMeta();
        var b = a with { Office = "Paris", FiscalYear = 2025 };

        Assert.Equal("Paris", b.Office);
        Assert.Equal(2025, b.FiscalYear);
        Assert.Equal(a.Id, b.Id);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DocumentChunk_carries_provenance_back_to_parent_document()
    {
        var meta = SampleMeta();
        var chunk = new DocumentChunk(
            ChunkId: "hr-001#0",
            DocumentId: meta.Id,
            ChunkIndex: 0,
            Text: "Employees receive 20 vacation days per fiscal year.",
            StartCharOffset: 0,
            EndCharOffset: 51,
            Metadata: meta);

        Assert.Equal(meta.Id, chunk.DocumentId);
        Assert.Equal(meta, chunk.Metadata);
        Assert.Equal(51, chunk.EndCharOffset - chunk.StartCharOffset);
    }

    [Fact]
    public void EmbeddedChunk_preserves_chunk_and_records_model_name()
    {
        var meta = SampleMeta();
        var chunk = new DocumentChunk("hr-001#0", meta.Id, 0, "text", 0, 4, meta);
        float[] data = [0.1f, 0.2f, 0.3f];
        var vector = new ReadOnlyMemory<float>(data);

        var embedded = new EmbeddedChunk(chunk, vector, EmbeddingModel: "nomic-embed-text");

        Assert.Same(chunk, embedded.Chunk);
        Assert.Equal(3, embedded.Vector.Length);
        Assert.Equal("nomic-embed-text", embedded.EmbeddingModel);
    }

    [Fact]
    public void RetrievalResult_can_be_sorted_by_score_descending()
    {
        var meta = SampleMeta();
        var c1 = new DocumentChunk("hr-001#0", meta.Id, 0, "a", 0, 1, meta);
        var c2 = new DocumentChunk("hr-001#1", meta.Id, 1, "b", 1, 2, meta);
        var c3 = new DocumentChunk("hr-001#2", meta.Id, 2, "c", 2, 3, meta);

        var unordered = new[]
        {
            new RetrievalResult(c2, 0.55),
            new RetrievalResult(c1, 0.91),
            new RetrievalResult(c3, 0.12),
        };

        var ordered = unordered.OrderByDescending(r => r.Score).ToArray();

        Assert.Equal(c1, ordered[0].Chunk);
        Assert.Equal(c2, ordered[1].Chunk);
        Assert.Equal(c3, ordered[2].Chunk);
    }
}
