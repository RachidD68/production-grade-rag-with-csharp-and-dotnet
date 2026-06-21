using SmartDocs.Core.Filtering;

namespace SmartDocs.Routing.Filtering;

/// <summary>
/// The caller's authorization scope, derived from the authenticated principal —
/// never from the LLM or the user's query text. <see cref="ToFilter"/> produces
/// the <see cref="MetadataFilter"/> that is ANDed into every search and must
/// never be relaxed: it enforces a confidentiality ceiling plus optional
/// office / silo scoping.
/// </summary>
/// <param name="ClearanceLevel">
/// The highest <c>ConfidentialityLevel</c> this caller may see. Ordered
/// Public &lt; Internal &lt; Restricted &lt; Confidential; the caller sees only
/// levels at or below their clearance.
/// </param>
/// <param name="Office">When set, restricts results to this office.</param>
/// <param name="Silo">When set, restricts results to this silo.</param>
public sealed record SecurityContext(string ClearanceLevel, string? Office = null, string? Silo = null)
{
    /// <summary>Confidentiality levels in ascending order of sensitivity.</summary>
    private static readonly string[] Levels = ["Public", "Internal", "Restricted", "Confidential"];

    /// <summary>
    /// Builds the always-on security filter: a confidentiality ceiling ANDed
    /// with office / silo equality scopes when provided. The ceiling is
    /// expressed as set membership over the allowed levels so it compiles to a
    /// single <c>should</c>/<c>OR</c> branch in the store while remaining a hard
    /// gate the relaxation loop can never widen.
    /// </summary>
    public MetadataFilter ToFilter()
    {
        var ceiling = ClampIndex(ClearanceLevel);
        // Allowed = every level at or below the caller's clearance.
        var allowed = Levels[..(ceiling + 1)];

        // m => allowed.Contains(m.ConfidentialityLevel)
        var filter = MetadataFilter.Where(m => allowed.Contains(m.ConfidentialityLevel));

        if (!string.IsNullOrWhiteSpace(Office))
        {
            var office = Office;
            filter = filter.And(MetadataFilter.Where(m => m.Office == office));
        }
        if (!string.IsNullOrWhiteSpace(Silo))
        {
            var silo = Silo;
            filter = filter.And(MetadataFilter.Where(m => m.Silo == silo));
        }
        return filter;
    }

    /// <summary>Index of the clearance level; an unknown level clamps to the most restrictive (Public).</summary>
    private static int ClampIndex(string level)
    {
        var idx = Array.IndexOf(Levels, level);
        return idx < 0 ? 0 : idx;
    }
}
