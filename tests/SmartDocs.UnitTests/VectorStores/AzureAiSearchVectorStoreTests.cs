using Azure.Search.Documents.Indexes.Models;
using SmartDocs.Core.Documents;
using SmartDocs.Retrieval.VectorStores;

namespace SmartDocs.UnitTests.VectorStores;

/// <summary>
/// Unit tests for <see cref="AzureAiSearchVectorStore"/> that exercise its
/// schema and document mapping without a live Azure AI Search service. The
/// live round-trip lives in the gated integration test
/// (<c>SmartDocs.IntegrationTests.AzureSearch.AzureAiSearchVectorStoreTests</c>).
/// </summary>
public sealed class AzureAiSearchVectorStoreTests
{
    private static EmbeddedChunk SampleChunk()
    {
        var meta = new DocumentMetadata(
            "doc-7", "hr-policies", "HR", "Montreal", "Internal", "Policy",
            2026, "Jane", new DateOnly(2026, 3, 14), "Vacation Policy");
        var chunk = new DocumentChunk("doc-7#2", "doc-7", 2, "Some chunk text", 10, 25, meta);
        return new EmbeddedChunk(chunk, new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f]), "test-model");
    }

    [Fact]
    public void BuildIndex_has_key_and_cosine_hnsw_vector_field()
    {
        var index = AzureAiSearchVectorStore.BuildIndex("ch06-idx", vectorSize: 768);

        Assert.Equal("ch06-idx", index.Name);

        var keyField = Assert.Single(index.Fields, f => f.IsKey == true);
        Assert.Equal(AzureAiSearchVectorStore.KeyFieldName, keyField.Name);

        var vectorField = Assert.Single(index.Fields, f => f.Name == AzureAiSearchVectorStore.VectorFieldName);
        Assert.Equal(768, vectorField.VectorSearchDimensions);
        Assert.Equal(AzureAiSearchVectorStore.VectorProfileName, vectorField.VectorSearchProfileName);

        var hnsw = Assert.Single(index.VectorSearch.Algorithms);
        var hnswConfig = Assert.IsType<HnswAlgorithmConfiguration>(hnsw);
        Assert.Equal(VectorSearchAlgorithmMetric.Cosine, hnswConfig.Parameters.Metric);

        var profile = Assert.Single(index.VectorSearch.Profiles);
        Assert.Equal(AzureAiSearchVectorStore.VectorProfileName, profile.Name);
    }

    [Fact]
    public void EncodeKey_is_deterministic_and_url_safe()
    {
        const string chunkId = "doc-7#2";
        var key1 = AzureAiSearchVectorStore.EncodeKey(chunkId);
        var key2 = AzureAiSearchVectorStore.EncodeKey(chunkId);

        Assert.Equal(key1, key2); // deterministic => idempotent upsert
        Assert.DoesNotContain('#', key1);
        Assert.DoesNotContain('+', key1);
        Assert.DoesNotContain('/', key1);
        Assert.DoesNotContain('=', key1);
    }

    [Fact]
    public void Document_roundtrips_text_and_metadata()
    {
        var original = SampleChunk();

        var doc = AzureAiSearchVectorStore.ToSearchDocument(original);
        var rebuilt = AzureAiSearchVectorStore.BuildChunk(doc);

        Assert.Equal(original.Chunk.ChunkId, rebuilt.ChunkId);
        Assert.Equal(original.Chunk.DocumentId, rebuilt.DocumentId);
        Assert.Equal(original.Chunk.ChunkIndex, rebuilt.ChunkIndex);
        Assert.Equal(original.Chunk.Text, rebuilt.Text);
        Assert.Equal(original.Chunk.StartCharOffset, rebuilt.StartCharOffset);
        Assert.Equal(original.Chunk.EndCharOffset, rebuilt.EndCharOffset);
        Assert.Equal(original.Chunk.Metadata.Department, rebuilt.Metadata.Department);
        Assert.Equal(original.Chunk.Metadata.FiscalYear, rebuilt.Metadata.FiscalYear);
        Assert.Equal(original.Chunk.Metadata.LastModified, rebuilt.Metadata.LastModified);
        Assert.Equal(original.Chunk.Metadata.Title, rebuilt.Metadata.Title);
    }
}
