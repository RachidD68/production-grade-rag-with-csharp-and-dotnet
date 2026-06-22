using System.ComponentModel;
using ModelContextProtocol.Server;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Mcp;

/// <summary>
/// The typed result of a <c>get_chunk</c> lookup. <see cref="Found"/> is
/// <see langword="false"/> both when no chunk has the requested id and when the
/// chunk exists but the caller's clearance does not cover it — the two cases are
/// deliberately indistinguishable to the caller so a denied lookup cannot be
/// used to probe which ids exist.
/// </summary>
/// <param name="Found">Whether an authorized chunk was returned.</param>
/// <param name="Chunk">The chunk when <see cref="Found"/> is <see langword="true"/>; otherwise <see langword="null"/>.</param>
/// <param name="Reason">A short human-readable status (<c>ok</c>, <c>not-found-or-denied</c>).</param>
public sealed record ChunkLookupResult(bool Found, DocumentChunk? Chunk, string Reason)
{
    /// <summary>The shared not-found / denied result (identical for both cases by design).</summary>
    public static ChunkLookupResult NotFoundOrDenied { get; } =
        new(false, null, "not-found-or-denied");

    /// <summary>An authorized hit carrying the resolved chunk.</summary>
    public static ChunkLookupResult Ok(DocumentChunk chunk) => new(true, chunk, "ok");
}

/// <summary>
/// MCP tool that resolves a single chunk by its stable id. After the lookup it
/// applies the boundary tenant scope (Option A): a chunk outside the principal's
/// clearance is reported as <see cref="ChunkLookupResult.NotFoundOrDenied"/>,
/// the same response as a genuinely missing id — closing the resource-leakage
/// gap where an authorized-but-unscoped read could exfiltrate a confidential
/// chunk by id.
/// </summary>
[McpServerToolType]
public sealed class GetChunkTool
{
    private readonly IChunkLookup _lookup;
    private readonly ITenantContext _tenant;

    /// <summary>Create the tool over the chunk lookup and the per-call tenant scope.</summary>
    public GetChunkTool(IChunkLookup lookup, ITenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(tenant);
        _lookup = lookup;
        _tenant = tenant;
    }

    /// <summary>Fetch the full text of a chunk by its stable ChunkId, subject to the caller's clearance.</summary>
    [McpServerTool(Name = "get_chunk", ReadOnly = true, OpenWorld = false), Description(
        "Fetch a single chunk by its stable ChunkId (e.g. 'hr-vacation#0'). Read-only. " +
        "Returns not-found-or-denied when the id does not exist or is outside the caller's clearance.")]
    public async Task<ChunkLookupResult> GetChunkAsync(
        [Description("The stable chunk identifier, e.g. 'hr-vacation#0'.")] string chunkId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);
        var chunk = await _lookup.GetByIdAsync(chunkId, cancellationToken).ConfigureAwait(false);
        if (chunk is null)
        {
            return ChunkLookupResult.NotFoundOrDenied;
        }

        // Boundary tenant scope AFTER lookup: deny chunks outside the clearance.
        var filter = _tenant.Security.ToFilter();
        return filter.Matches(chunk.Metadata)
            ? ChunkLookupResult.Ok(chunk)
            : ChunkLookupResult.NotFoundOrDenied;
    }
}
