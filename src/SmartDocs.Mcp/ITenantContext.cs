using SmartDocs.Routing.Filtering;

namespace SmartDocs.Mcp;

/// <summary>
/// The per-request tenant / authorization scope an MCP tool enforces at the
/// server boundary. This is the Option-A seam (see Chapter 18): rather than
/// changing <c>IRetriever.RetrieveAsync(query, topK, ct)</c> or introducing a
/// dedicated retrieval-filter parameter type, every tool resolves an
/// <see cref="ITenantContext"/> from DI and applies
/// <c>Security.ToFilter().Matches(metadata)</c> to each hit
/// <em>after</em> retrieval — a hard gate derived from the authenticated
/// principal, never from the query text or the calling LLM.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The caller's authorization scope, reusing Chapter 11's
    /// <see cref="SecurityContext"/> (a confidentiality ceiling plus optional
    /// office / silo scoping). Its <see cref="SecurityContext.ToFilter"/>
    /// produces the <c>MetadataFilter</c> the tools apply at the boundary.
    /// </summary>
    SecurityContext Security { get; }
}

/// <summary>
/// A fixed, full-access tenant context for local development and the offline
/// stdio sample — there is no authenticated principal on stdio, so the dev
/// default grants the highest clearance and no office / silo scope. Production
/// hosts replace this with a claims-derived implementation built from the
/// authenticated <c>ClaimsPrincipal</c>.
/// </summary>
public sealed class FixedTenantContext : ITenantContext
{
    /// <summary>Create a tenant context that always reports <paramref name="security"/>.</summary>
    /// <param name="security">
    /// The fixed scope. Defaults to <c>Confidential</c> clearance with no office
    /// or silo restriction — i.e. full read access — when not supplied.
    /// </param>
    public FixedTenantContext(SecurityContext? security = null)
    {
        Security = security ?? new SecurityContext("Confidential");
    }

    /// <inheritdoc />
    public SecurityContext Security { get; }
}
