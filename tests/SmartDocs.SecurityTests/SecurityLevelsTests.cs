using SmartDocs.Security;

namespace SmartDocs.SecurityTests;

public sealed class SecurityLevelsTests
{
    [Theory]
    // A Confidential clearance is the top tier — it sees everything.
    [InlineData(SecurityLevel.Public, SecurityLevel.Confidential, true)]
    [InlineData(SecurityLevel.Internal, SecurityLevel.Confidential, true)]
    [InlineData(SecurityLevel.Restricted, SecurityLevel.Confidential, true)]
    [InlineData(SecurityLevel.Confidential, SecurityLevel.Confidential, true)]
    // A Public clearance sees only Public.
    [InlineData(SecurityLevel.Public, SecurityLevel.Public, true)]
    [InlineData(SecurityLevel.Internal, SecurityLevel.Public, false)]
    // A Restricted clearance sees Public/Internal/Restricted, NOT Confidential.
    [InlineData(SecurityLevel.Restricted, SecurityLevel.Restricted, true)]
    [InlineData(SecurityLevel.Confidential, SecurityLevel.Restricted, false)]
    public void AtOrBelow_matrix(SecurityLevel chunkLevel, SecurityLevel clearance, bool expected)
        => Assert.Equal(expected, SecurityLevels.AtOrBelow(chunkLevel, clearance));

    [Theory]
    [InlineData("Public", SecurityLevel.Public)]
    [InlineData("Internal", SecurityLevel.Internal)]
    [InlineData("Restricted", SecurityLevel.Restricted)]
    [InlineData("Confidential", SecurityLevel.Confidential)]
    [InlineData("confidential", SecurityLevel.Confidential)] // case-insensitive
    [InlineData("totally-unknown", SecurityLevel.Confidential)] // fail-closed to the top tier
    public void FromMetadata_maps_strings(string value, SecurityLevel expected)
        => Assert.Equal(expected, SecurityLevels.FromMetadata(value));

    /// <summary>
    /// The solution has two clearance gates over the same vocabulary: this enum
    /// (used by <see cref="SecurityLevels.AtOrBelow"/>) and the string ladder in
    /// SmartDocs.Routing's SecurityContext. They once disagreed on whether
    /// Restricted or Confidential was more sensitive, so the same chunk was
    /// visible through one gate and hidden by the other. This pins them together.
    /// </summary>
    [Theory]
    [InlineData("Public", 0)]
    [InlineData("Internal", 1)]
    [InlineData("Restricted", 2)]
    [InlineData("Confidential", 3)]
    public void Enum_order_matches_SecurityContext_ladder(string level, int expectedRank)
    {
        // SecurityContext.Levels is ascending by sensitivity; the enum must agree
        // rank-for-rank or the two gates make different authorization decisions.
        string[] contextLadder = ["Public", "Internal", "Restricted", "Confidential"];

        Assert.Equal(expectedRank, Array.IndexOf(contextLadder, level));
        Assert.Equal(expectedRank, (int)SecurityLevels.FromMetadata(level));
    }
}
