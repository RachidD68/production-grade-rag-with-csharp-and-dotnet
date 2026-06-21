using Azure;
using Azure.Search.Documents.Indexes;
using SmartDocs.Core.Documents;
using SmartDocs.Retrieval.VectorStores;

namespace SmartDocs.IntegrationTests.AzureSearch;

/// <summary>
/// Integration tests for <see cref="AzureAiSearchVectorStore"/>. Gated behind
/// <c>RUN_AZURE_SEARCH_INTEGRATION=1</c> plus the connection env vars
/// <c>AZURE_SEARCH_ENDPOINT</c> / <c>AZURE_SEARCH_API_KEY</c>. Mirrors the
/// Qdrant integration gating so CI stays green without an Azure account.
/// </summary>
[Trait("Category", "RealAzureSearch")]
public sealed class AzureAiSearchVectorStoreTests
{
    private const string GateVar = "RUN_AZURE_SEARCH_INTEGRATION";

    [Fact]
    public async Task Roundtrip_upsert_search_delete_against_real_azure_search()
    {
        if (Environment.GetEnvironmentVariable(GateVar) != "1")
        {
            Console.WriteLine(
                "[SKIP] set RUN_AZURE_SEARCH_INTEGRATION=1 + AZURE_SEARCH_ENDPOINT/AZURE_SEARCH_API_KEY to run");
            return;
        }

        var endpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT");
        var apiKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(endpoint), "AZURE_SEARCH_ENDPOINT must be set when the gate is on.");
        Assert.False(string.IsNullOrWhiteSpace(apiKey), "AZURE_SEARCH_API_KEY must be set when the gate is on.");

        var indexName = $"smartdocs-itest-{Guid.NewGuid():N}";
        var credential = new AzureKeyCredential(apiKey!);
        var store = new AzureAiSearchVectorStore(new Uri(endpoint!), indexName, vectorSize: 3, credential);
        var indexClient = new SearchIndexClient(new Uri(endpoint!), credential);

        try
        {
            await store.EnsureCollectionExistsAsync();

            var meta = new DocumentMetadata("doc-1", "hr-policies", "HR", "Montreal",
                "Internal", "Policy", 2026, "Author", new DateOnly(2026, 1, 1), "Test");
            var c1 = new EmbeddedChunk(
                new DocumentChunk("doc-1#0", "doc-1", 0, "first chunk text", 0, 16, meta),
                new ReadOnlyMemory<float>([1f, 0f, 0f]),
                "test-model");
            var c2 = new EmbeddedChunk(
                new DocumentChunk("doc-1#1", "doc-1", 1, "second chunk text", 16, 33, meta),
                new ReadOnlyMemory<float>([0f, 1f, 0f]),
                "test-model");

            await store.UpsertAsync([c1, c2]);
            await Task.Delay(TimeSpan.FromSeconds(3)); // indexing is eventually consistent

            var hits = await store.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f]), topK: 2);

            Assert.Equal(2, hits.Count);
            Assert.Equal("doc-1#0", hits[0].Chunk.ChunkId);
            Assert.Equal("first chunk text", hits[0].Chunk.Text);
            Assert.Equal("HR", hits[0].Chunk.Metadata.Department);
            Assert.Equal(2026, hits[0].Chunk.Metadata.FiscalYear);

            await store.DeleteAsync(["doc-1#0"]);
            await Task.Delay(TimeSpan.FromSeconds(3));
            var afterDelete = await store.SearchAsync(new ReadOnlyMemory<float>([1f, 0f, 0f]), topK: 2);
            Assert.Single(afterDelete);
            Assert.Equal("doc-1#1", afterDelete[0].Chunk.ChunkId);
        }
        finally
        {
            try
            {
                await indexClient.DeleteIndexAsync(indexName, new Azure.MatchConditions(), CancellationToken.None);
            }
            catch { /* best-effort cleanup */ }
        }
    }
}
