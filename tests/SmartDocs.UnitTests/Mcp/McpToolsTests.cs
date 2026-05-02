using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Mcp;

namespace SmartDocs.UnitTests.Mcp;

public sealed class McpToolsTests
{
    [Fact]
    public async Task Search_returns_serialised_results_after_Configure()
    {
        var meta = new DocumentMetadata("hr-001", "hr-policies", "HR", "Montreal",
            "Internal", "Policy", 2026, "x", new DateOnly(2026, 1, 1), "Annual Leave");
        var hit = new RetrievalResult(
            new DocumentChunk("hr-001#0", "hr-001", 0, "20 days vacation", 0, 16, meta),
            0.91);
        var retriever = new ConstantRetriever([hit]);
        SmartDocsMcpTools.Configure(retriever);

        var json = await SmartDocsMcpTools.Search("vacation", topK: 5);

        Assert.Contains("hr-001#0", json, StringComparison.Ordinal);
        Assert.Contains("Annual Leave", json, StringComparison.Ordinal);
        Assert.Contains("20 days vacation", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ingest_returns_a_tracking_id()
    {
        var json = await SmartDocsMcpTools.Ingest("some content", "hr-policies");
        Assert.Contains("trackingId", json, StringComparison.Ordinal);
        Assert.Contains("queued", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetChunk_returns_a_stub_payload()
    {
        var json = await SmartDocsMcpTools.GetChunk("hr-001#0");
        Assert.Contains("hr-001#0", json, StringComparison.Ordinal);
    }

    private sealed class ConstantRetriever : IRetriever
    {
        private readonly IReadOnlyList<RetrievalResult> _r;
        public ConstantRetriever(IReadOnlyList<RetrievalResult> r) { _r = r; }
        public string Strategy => "stub";
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string q, int k, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([.. _r.Take(k)]);
    }
}
