using System.ComponentModel;
using ModelContextProtocol.Server;
using SmartDocs.Core.Documents;
using SmartDocs.Retrieval.Graph;

namespace SmartDocs.Mcp;

/// <summary>
/// MCP tool exposing the Chapter 17 LazyGraphRAG retriever — community / subgraph
/// summaries for questions that span entities and relationships rather than a
/// single passage. Like <see cref="SearchTool"/>, results pass the boundary
/// tenant scope before they leave the server.
/// </summary>
[McpServerToolType]
public sealed class GraphSearchTool
{
    private readonly LazyGraphRagRetriever _graph;
    private readonly ITenantContext _tenant;

    /// <summary>Create the tool over the LazyGraphRAG retriever and the per-call tenant scope.</summary>
    public GraphSearchTool(LazyGraphRagRetriever graph, ITenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(tenant);
        _graph = graph;
        _tenant = tenant;
    }

    /// <summary>
    /// Answer a relationship-spanning question with a LazyGraphRAG subgraph
    /// summary, scoped to the caller's clearance.
    /// </summary>
    [McpServerTool(Name = "graph_search", ReadOnly = true, OpenWorld = false), Description(
        "Search the SmartDocs knowledge graph and return LazyGraphRAG subgraph summaries " +
        "for questions that span multiple entities or relationships. Read-only. " +
        "Results are scoped to the caller's clearance.")]
    public async Task<IReadOnlyList<RetrievalResult>> GraphSearchAsync(
        [Description("The user's natural-language query.")] string query,
        [Description("How many summaries to return (default 5, max 25).")] int k = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var capped = Math.Clamp(k, 1, 25);
        var hits = await _graph.RetrieveAsync(query, capped, cancellationToken).ConfigureAwait(false);
        var filter = _tenant.Security.ToFilter();
        return [.. hits.Where(h => filter.Matches(h.Chunk.Metadata))];
    }
}
