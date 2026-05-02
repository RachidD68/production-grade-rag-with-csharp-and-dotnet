using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SmartDocs.Core.Abstractions;

namespace SmartDocs.Mcp;

/// <summary>
/// MCP tool surface that exposes the SmartDocs retrieval pipeline. Other
/// MCP-aware clients (Claude Desktop, ChatGPT, GitHub Copilot, MAF agents
/// via HostedMcpTool) can invoke these tools to query the knowledge base.
///
/// Wire up by calling
/// <c>builder.Services.AddMcpServer().WithToolsFromAssembly()</c> in the
/// Mcp host project (Phase 7 capstone) and exposing it over stdio or HTTP.
/// </summary>
[McpServerToolType]
public static class SmartDocsMcpTools
{
    private static IRetriever? _retriever;

    /// <summary>One-time configuration. Call from the host's startup.</summary>
    public static void Configure(IRetriever retriever)
    {
        ArgumentNullException.ThrowIfNull(retriever);
        _retriever = retriever;
    }

    [McpServerTool, Description("Search the SmartDocs knowledge base. Returns up to topK relevant chunks with citations.")]
    public static async Task<string> Search(
        [Description("The natural-language search query.")] string query,
        [Description("How many results to return (default 5, max 25).")] int topK = 5)
    {
        var retriever = _retriever ?? throw new InvalidOperationException(
            "SmartDocsMcpTools.Configure(retriever) must be called at startup.");
        var capped = Math.Clamp(topK, 1, 25);
        var hits = await retriever.RetrieveAsync(query, capped).ConfigureAwait(false);
        var payload = hits.Select(h => new
        {
            chunkId = h.Chunk.ChunkId,
            documentId = h.Chunk.DocumentId,
            title = h.Chunk.Metadata.Title,
            silo = h.Chunk.Metadata.Silo,
            score = h.Score,
            text = h.Chunk.Text,
        }).ToArray();
        return JsonSerializer.Serialize(payload);
    }

    [McpServerTool, Description("Fetch the full text of a chunk by its stable ChunkId.")]
    public static Task<string> GetChunk(
        [Description("The stable chunk identifier (e.g. hr-001#0).")] string chunkId)
    {
        // For Phase 5 we resolve via the retriever's own search — a real
        // implementation in Phase 7 talks to the document store directly.
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);
        return Task.FromResult($"{{\"chunkId\":\"{chunkId}\",\"note\":\"Direct chunk lookup wired in Phase 7 capstone.\"}}");
    }

    [McpServerTool, Description("Stub: signal that an external client wants to ingest a document. Returns a tracking id.")]
    public static Task<string> Ingest(
        [Description("Free-text content to ingest.")] string content,
        [Description("Logical silo (e.g. hr-policies, technical-docs).")] string silo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(silo);
        var trackingId = Guid.NewGuid().ToString("N")[..12];
        return Task.FromResult($"{{\"trackingId\":\"{trackingId}\",\"silo\":\"{silo}\",\"status\":\"queued\",\"note\":\"Phase 7 wires the actual ingestion pipeline.\"}}");
    }
}
