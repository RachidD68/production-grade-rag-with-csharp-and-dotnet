using SmartDocs.Core.Documents;
using SmartDocs.Security.Retrieval;

namespace SmartDocs.SecurityTests;

public sealed class TenantGuardTests
{
    private static readonly DocumentMetadata Meta = new(
        Id: "d", Silo: "s", Department: "dep", Office: "off",
        ConfidentialityLevel: "Internal", DocumentType: "Policy", FiscalYear: 2026,
        Author: "a", LastModified: new DateOnly(2026, 1, 1), Title: "t");

    private static RetrievalResult Result(string chunkId)
        => new(new DocumentChunk(chunkId, "d", 0, "text", 0, 4, Meta), Score: 0.9);

    [Fact]
    public async Task Cross_tenant_chunk_is_dropped_and_incident_raised()
    {
        var index = new DictionaryTenantIndex(new Dictionary<string, string>
        {
            ["mine"] = "tenant-A",
            ["theirs"] = "tenant-B",
        });
        var sink = new CapturingAlertSink();
        var guard = new TenantGuard(index, sink);

        var survivors = await guard.VerifyAsync(
            [Result("mine"), Result("theirs")],
            expectedTenant: "tenant-A",
            CancellationToken.None);

        Assert.Single(survivors);
        Assert.Equal("mine", survivors[0].Chunk.ChunkId);
        var incident = Assert.Single(sink.Incidents);
        Assert.Equal("tenant-leak-prevented", incident.Kind);
    }

    [Fact]
    public async Task Unknown_chunk_is_dropped()
    {
        var index = new DictionaryTenantIndex(new Dictionary<string, string> { ["mine"] = "tenant-A" });
        var sink = new CapturingAlertSink();
        var guard = new TenantGuard(index, sink);

        var survivors = await guard.VerifyAsync(
            [Result("ghost")], expectedTenant: "tenant-A", CancellationToken.None);

        Assert.Empty(survivors);
        Assert.Single(sink.Incidents);
    }

    [Fact]
    public async Task Same_tenant_chunk_survives_without_incident()
    {
        var index = new DictionaryTenantIndex(new Dictionary<string, string> { ["mine"] = "tenant-A" });
        var sink = new CapturingAlertSink();
        var guard = new TenantGuard(index, sink);

        var survivors = await guard.VerifyAsync(
            [Result("mine")], expectedTenant: "tenant-A", CancellationToken.None);

        Assert.Single(survivors);
        Assert.Empty(sink.Incidents);
    }
}
