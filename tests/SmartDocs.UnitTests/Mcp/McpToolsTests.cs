using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Mcp;
using SmartDocs.Reranking;
using SmartDocs.Routing.Filtering;

namespace SmartDocs.UnitTests.Mcp;

/// <summary>
/// Unit tests for the constructor-DI MCP tool surface. Each tool is constructed
/// directly with stub dependencies (no MCP host) and its method invoked, so the
/// tests assert the tool's contract — typed results, by-id lookup, the boundary
/// tenant check, and the ingest write-tool — deterministically and offline.
/// </summary>
public sealed class McpToolsTests
{
    private static DocumentChunk Chunk(string id, string text, string confidentiality = "Internal", string silo = "hr-policies")
    {
        var meta = new DocumentMetadata(id, silo, "HR", "Montreal",
            confidentiality, "Policy", 2026, "SmartDocs", new DateOnly(2026, 1, 1), $"Title {id}");
        return new DocumentChunk($"{id}#0", id, 0, text, 0, text.Length, meta);
    }

    private static RetrievalResult Hit(DocumentChunk chunk, double score = 0.9) => new(chunk, score);

    // Full-access dev tenant (Confidential clearance, no office/silo scope).
    private static FixedTenantContext FullAccess() => new(new SecurityContext("Confidential"));

    // Public-clearance tenant — sees only Public chunks.
    private static FixedTenantContext PublicOnly() => new(new SecurityContext("Public"));

    [Fact]
    public async Task Search_returns_typed_hits_scoped_to_tenant()
    {
        var hit = Hit(Chunk("hr-vacation", "20 days vacation"));
        var tool = new SearchTool(new ConstantRetriever([hit]), new PassThroughReranker(), FullAccess());

        var results = await tool.SearchAsync("vacation", k: 5);

        var single = Assert.Single(results);
        Assert.Equal("hr-vacation#0", single.Chunk.ChunkId);
        Assert.Equal("20 days vacation", single.Chunk.Text);
    }

    [Fact]
    public async Task SearchAndRerank_returns_typed_reranked_hits()
    {
        var a = Hit(Chunk("hr-a", "alpha"), 0.5);
        var b = Hit(Chunk("hr-b", "bravo"), 0.4);
        // PassThroughReranker preserves order; assert both survive and are typed.
        var tool = new SearchTool(new ConstantRetriever([a, b]), new PassThroughReranker(), FullAccess());

        var results = await tool.SearchAndRerankAsync("anything", k: 5);

        Assert.Equal(2, results.Count);
        Assert.Equal("hr-a#0", results[0].Chunk.ChunkId);
    }

    [Fact]
    public async Task Search_drops_hits_outside_the_principal_clearance()
    {
        var publicHit = Hit(Chunk("pub", "public text", confidentiality: "Public"));
        var confidentialHit = Hit(Chunk("secret", "confidential text", confidentiality: "Confidential"));
        var tool = new SearchTool(
            new ConstantRetriever([publicHit, confidentialHit]), new PassThroughReranker(), PublicOnly());

        var results = await tool.SearchAsync("anything", k: 10);

        var single = Assert.Single(results);
        Assert.Equal("pub#0", single.Chunk.ChunkId);
    }

    [Fact]
    public async Task GetChunk_returns_the_chunk_by_id()
    {
        var chunk = Chunk("hr-vacation", "20 days vacation");
        var lookup = new InMemoryChunkLookup([chunk]);
        var tool = new GetChunkTool(lookup, FullAccess());

        var result = await tool.GetChunkAsync("hr-vacation#0");

        Assert.True(result.Found);
        Assert.NotNull(result.Chunk);
        Assert.Equal("20 days vacation", result.Chunk!.Text);
    }

    [Fact]
    public async Task GetChunk_returns_not_found_for_unknown_id()
    {
        var tool = new GetChunkTool(new InMemoryChunkLookup(), FullAccess());

        var result = await tool.GetChunkAsync("does-not-exist#0");

        Assert.False(result.Found);
        Assert.Null(result.Chunk);
    }

    [Fact]
    public async Task GetChunk_denies_a_chunk_outside_the_principal_clearance()
    {
        var confidential = Chunk("secret", "confidential text", confidentiality: "Confidential");
        var lookup = new InMemoryChunkLookup([confidential]);
        var tool = new GetChunkTool(lookup, PublicOnly());

        var result = await tool.GetChunkAsync("secret#0");

        // Denied — and indistinguishable from not-found by design.
        Assert.False(result.Found);
        Assert.Null(result.Chunk);
        Assert.Equal("not-found-or-denied", result.Reason);
    }

    [Fact]
    public async Task ChunkResource_returns_text_for_an_authorized_chunk()
    {
        var chunk = Chunk("hr-vacation", "20 days vacation");
        var resource = new ChunkResource(new InMemoryChunkLookup([chunk]), FullAccess());

        var contents = await resource.ReadChunkAsync("hr-vacation#0");

        Assert.Equal("smartdocs://chunk/hr-vacation#0", contents.Uri);
        Assert.Equal("20 days vacation", contents.Text);
    }

    [Fact]
    public async Task ChunkResource_denies_a_chunk_outside_the_principal_clearance()
    {
        var confidential = Chunk("secret", "confidential text", confidentiality: "Confidential");
        var resource = new ChunkResource(new InMemoryChunkLookup([confidential]), PublicOnly());

        var contents = await resource.ReadChunkAsync("secret#0");

        // The body must NOT leak the confidential text.
        Assert.DoesNotContain("confidential text", contents.Text, StringComparison.Ordinal);
        Assert.Contains("not authorized", contents.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GraphSearch_returns_typed_hits()
    {
        // LazyGraphRagRetriever needs an EntityExtractor + IGraphStore + IChatClient;
        // exercising it fully is the integration sample's job. Here we assert the
        // tool's boundary behaviour with a stub graph retriever via the shared seam.
        var graphHit = Hit(Chunk("graph", "subgraph summary"));
        var tool = new GraphSearchToolHarness([graphHit], FullAccess());

        var results = await tool.RunAsync("how do teams relate", 5);

        var single = Assert.Single(results);
        Assert.Equal("graph#0", single.Chunk.ChunkId);
    }

    [Fact]
    public async Task Ingest_returns_a_tracking_id()
    {
        var sink = new InMemoryIngestSink();
        var tool = new IngestTool(sink);

        var ticket = await tool.IngestAsync("some content", "hr-policies");

        Assert.False(string.IsNullOrWhiteSpace(ticket.TrackingId));
        Assert.Equal("hr-policies", ticket.Silo);
        Assert.Equal("queued", ticket.Status);
        Assert.Single(sink.Queued);
    }

    // --- Stubs ---------------------------------------------------------------

    private sealed class ConstantRetriever(IReadOnlyList<RetrievalResult> results) : IRetriever
    {
        public string Strategy => "stub";
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string query, int topK, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([.. results.Take(topK)]);
    }

    private sealed class PassThroughReranker : IReranker
    {
        public string Implementation => "stub-passthrough";
        public Task<IReadOnlyList<RetrievalResult>> RerankAsync(
            string query, IReadOnlyList<RetrievalResult> candidates, int topK, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([.. candidates.Take(topK)]);
    }

    // GraphSearchTool depends on the concrete LazyGraphRagRetriever, which is not
    // trivially stubbable; this harness mirrors GraphSearchTool's boundary logic
    // over a constant hit list so the tenant-scoping contract is still asserted in
    // a unit test. The end-to-end LazyGraphRAG path is covered by the Ch17 sample.
    private sealed class GraphSearchToolHarness(IReadOnlyList<RetrievalResult> hits, ITenantContext tenant)
    {
        public Task<IReadOnlyList<RetrievalResult>> RunAsync(string query, int k)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(query);
            var filter = tenant.Security.ToFilter();
            IReadOnlyList<RetrievalResult> scoped = [.. hits.Take(k).Where(h => filter.Matches(h.Chunk.Metadata))];
            return Task.FromResult(scoped);
        }
    }
}
