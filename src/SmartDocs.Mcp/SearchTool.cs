using System.ComponentModel;
using ModelContextProtocol.Server;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Reranking;

namespace SmartDocs.Mcp;

/// <summary>
/// MCP tool surface over the SmartDocs retrieval pipeline. Registered via
/// <c>WithToolsFromAssembly()</c> / <c>WithTools&lt;SearchTool&gt;()</c>; the SDK
/// constructs one instance per call from DI, so the constructor parameters are
/// resolved fresh each invocation — which is exactly how the per-request
/// <see cref="ITenantContext"/> boundary scope is applied.
/// <para>
/// Tenant scoping is Option A (Chapter 18): retrieve first, then drop every hit
/// whose metadata fails <c>tenant.Security.ToFilter().Matches(...)</c>. No
/// dedicated retrieval-filter parameter type and no change to
/// <see cref="IRetriever"/> — the gate is derived from the authenticated
/// principal, never the query.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class SearchTool
{
    private readonly IRetriever _retriever;
    private readonly IReranker _reranker;
    private readonly ITenantContext _tenant;

    /// <summary>Create the tool over the retrieval pipeline and the per-call tenant scope.</summary>
    public SearchTool(IRetriever retriever, IReranker reranker, ITenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(retriever);
        ArgumentNullException.ThrowIfNull(reranker);
        ArgumentNullException.ThrowIfNull(tenant);
        _retriever = retriever;
        _reranker = reranker;
        _tenant = tenant;
    }

    /// <summary>
    /// Plain top-K vector search, no reranking. Returns the most relevant chunks
    /// the calling principal is cleared to see.
    /// </summary>
    [McpServerTool(Name = "search", ReadOnly = true, OpenWorld = false), Description(
        "Search the SmartDocs corpus and return up to K of the most relevant chunks. " +
        "Read-only. Results are scoped to the caller's clearance.")]
    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        [Description("The user's natural-language query.")] string query,
        [Description("How many chunks to return (default 5, max 25).")] int k = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var capped = Math.Clamp(k, 1, 25);
        var hits = await _retriever.RetrieveAsync(query, capped, cancellationToken).ConfigureAwait(false);
        return ScopeToTenant(hits);
    }

    /// <summary>
    /// The default search path: retrieve a wider candidate set, rerank precisely
    /// down to K, then scope to the caller's clearance. Implements the
    /// production-default <em>retrieve-broadly, rerank-precisely</em> pattern.
    /// </summary>
    [McpServerTool(Name = "search_and_rerank", ReadOnly = true, OpenWorld = false), Description(
        "Search the SmartDocs corpus and return the top-K reranked chunks (the default, " +
        "highest-quality search). Read-only. Results are scoped to the caller's clearance.")]
    public async Task<IReadOnlyList<RetrievalResult>> SearchAndRerankAsync(
        [Description("The user's natural-language query.")] string query,
        [Description("How many chunks to return (default 5, max 25).")] int k = 5,
        [Description("How many candidates to retrieve before reranking (default 20).")] int candidateCount = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var capped = Math.Clamp(k, 1, 25);
        var candidates = Math.Max(candidateCount, capped);
        var pool = await _retriever.RetrieveAsync(query, candidates, cancellationToken).ConfigureAwait(false);
        var reranked = await _reranker.RerankAsync(query, pool, capped, cancellationToken).ConfigureAwait(false);
        return ScopeToTenant(reranked);
    }

    // Boundary tenant scope (Option A): drop every hit the principal is not
    // cleared to see. Applied AFTER retrieval/reranking, on the chunk metadata.
    private IReadOnlyList<RetrievalResult> ScopeToTenant(IReadOnlyList<RetrievalResult> hits)
    {
        var filter = _tenant.Security.ToFilter();
        return [.. hits.Where(h => filter.Matches(h.Chunk.Metadata))];
    }
}
