using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SmartDocs.Core.Abstractions;

namespace SmartDocs.Mcp;

/// <summary>
/// MCP resource exposing chunk text by id through the resource <em>template</em>
/// <c>smartdocs://chunk/{id}</c>. Registered via <c>WithResources&lt;ChunkResource&gt;()</c>
/// / <c>WithResourcesFromAssembly()</c>; like the tools, the SDK constructs one
/// instance per read from DI so the per-request <see cref="ITenantContext"/> is
/// resolved fresh.
/// <para>
/// The same boundary tenant scope as <see cref="GetChunkTool"/> applies <em>before</em>
/// the text is returned: a chunk outside the caller's clearance reads back the
/// same "not found or not authorized" body as a missing id, closing the
/// resource-leakage gap where a raw resource read could exfiltrate a confidential
/// chunk by id.
/// </para>
/// </summary>
[McpServerResourceType]
public sealed class ChunkResource
{
    private readonly IChunkLookup _lookup;
    private readonly ITenantContext _tenant;

    /// <summary>Create the resource over the chunk lookup and the per-read tenant scope.</summary>
    public ChunkResource(IChunkLookup lookup, ITenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(tenant);
        _lookup = lookup;
        _tenant = tenant;
    }

    /// <summary>Read a chunk's text by id, subject to the caller's clearance.</summary>
    [McpServerResource(UriTemplate = "smartdocs://chunk/{id}", Name = "chunk", MimeType = "text/plain"),
     Description("The full text of a SmartDocs chunk, addressed by its stable ChunkId. " +
                 "Scoped to the caller's clearance.")]
    public async Task<TextResourceContents> ReadChunkAsync(
        [Description("The stable chunk identifier, e.g. 'hr-vacation#0'.")] string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var uri = $"smartdocs://chunk/{id}";
        var chunk = await _lookup.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        // Deny (and conflate with not-found) anything outside the clearance,
        // BEFORE returning the text — resource-leakage mitigation.
        if (chunk is null || !_tenant.Security.ToFilter().Matches(chunk.Metadata))
        {
            return new TextResourceContents
            {
                Uri = uri,
                MimeType = "text/plain",
                Text = $"Chunk '{id}' was not found or is not authorized for this caller.",
            };
        }

        return new TextResourceContents
        {
            Uri = uri,
            MimeType = "text/plain",
            Text = chunk.Text,
        };
    }
}
