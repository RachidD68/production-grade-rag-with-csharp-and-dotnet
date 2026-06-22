using System.Security.Claims;
using SmartDocs.Mcp;
using SmartDocs.Routing.Filtering;

namespace RagInDotNet.Samples.Ch18_McpHttpServer;

/// <summary>
/// HTTP <see cref="ITenantContext"/> that derives the caller's
/// <see cref="SecurityContext"/> from the authenticated
/// <see cref="ClaimsPrincipal"/> on the current request — the Option-A boundary
/// scope for the production host. Clearance, silo, and office come from claims
/// (<c>clearance</c>, <c>silo</c>, <c>office</c>), never from the request body or
/// the calling model. An unauthenticated request collapses to the most
/// restrictive scope (Public clearance), which the security filter then enforces.
/// </summary>
internal sealed class ClaimsTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public ClaimsTenantContext(IHttpContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <inheritdoc />
    public SecurityContext Security
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                // No principal → most restrictive scope.
                return new SecurityContext("Public");
            }

            var clearance = user.FindFirstValue("clearance") ?? "Public";
            var silo = user.FindFirstValue("silo");
            var office = user.FindFirstValue("office");
            return new SecurityContext(
                clearance,
                Office: string.IsNullOrWhiteSpace(office) ? null : office,
                Silo: string.IsNullOrWhiteSpace(silo) ? null : silo);
        }
    }
}
