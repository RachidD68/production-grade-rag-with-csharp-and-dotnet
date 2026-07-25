namespace SmartDocs.Security;

/// <summary>
/// Sensitivity ladder for documents and user clearances, ascending. A user may
/// see a chunk only when the chunk's level is at or below the user's clearance.
/// </summary>
public enum SecurityLevel
{
    /// <summary>Freely shareable.</summary>
    Public = 0,

    /// <summary>Internal-only; visible to authenticated members of the org.</summary>
    Internal = 1,

    /// <summary>Restricted; need-to-know within the org.</summary>
    Restricted = 2,

    /// <summary>Confidential; the most sensitive tier.</summary>
    Confidential = 3,
}

/// <summary>
/// Helpers for comparing <see cref="SecurityLevel"/> values and mapping the
/// free-form <c>DocumentMetadata.ConfidentialityLevel</c> string onto the enum.
/// </summary>
public static class SecurityLevels
{
    /// <summary>
    /// True when a chunk at <paramref name="chunkLevel"/> may be shown to a user
    /// holding <paramref name="clearance"/> — i.e. the chunk is no more sensitive
    /// than the clearance.
    /// </summary>
    public static bool AtOrBelow(SecurityLevel chunkLevel, SecurityLevel clearance)
        => chunkLevel <= clearance;

    /// <summary>
    /// Maps a <c>DocumentMetadata.ConfidentialityLevel</c> string
    /// (<c>Public</c> / <c>Internal</c> / <c>Restricted</c> / <c>Confidential</c>)
    /// to a <see cref="SecurityLevel"/>. Unknown values map to the most
    /// restrictive level so an unclassified document is never over-shared.
    /// </summary>
    public static SecurityLevel FromMetadata(string confidentialityLevel)
    {
        ArgumentNullException.ThrowIfNull(confidentialityLevel);
        return confidentialityLevel.Trim().ToLowerInvariant() switch
        {
            "public" => SecurityLevel.Public,
            "internal" => SecurityLevel.Internal,
            "restricted" => SecurityLevel.Restricted,
            "confidential" => SecurityLevel.Confidential,
            // Fail closed: the most sensitive tier is Confidential, so an
            // unclassified document is treated as Confidential, not merely
            // Restricted. (Before the ladder was reconciled with
            // SecurityContext.Levels, this line said Restricted — which under
            // the corrected ordering would have UNDER-classified the document.)
            _ => SecurityLevel.Confidential,
        };
    }
}
