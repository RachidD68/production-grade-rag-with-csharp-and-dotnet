using SmartDocs.Security;

namespace SmartDocs.SecurityTests;

public sealed class SecurityLevelsTests
{
    [Theory]
    // A Confidential clearance sees Public/Internal/Confidential, not Restricted.
    [InlineData(SecurityLevel.Public, SecurityLevel.Confidential, true)]
    [InlineData(SecurityLevel.Internal, SecurityLevel.Confidential, true)]
    [InlineData(SecurityLevel.Confidential, SecurityLevel.Confidential, true)]
    [InlineData(SecurityLevel.Restricted, SecurityLevel.Confidential, false)]
    // A Public clearance sees only Public.
    [InlineData(SecurityLevel.Public, SecurityLevel.Public, true)]
    [InlineData(SecurityLevel.Internal, SecurityLevel.Public, false)]
    // A Restricted clearance sees everything.
    [InlineData(SecurityLevel.Restricted, SecurityLevel.Restricted, true)]
    [InlineData(SecurityLevel.Confidential, SecurityLevel.Restricted, true)]
    public void AtOrBelow_matrix(SecurityLevel chunkLevel, SecurityLevel clearance, bool expected)
        => Assert.Equal(expected, SecurityLevels.AtOrBelow(chunkLevel, clearance));

    [Theory]
    [InlineData("Public", SecurityLevel.Public)]
    [InlineData("Internal", SecurityLevel.Internal)]
    [InlineData("Confidential", SecurityLevel.Confidential)]
    [InlineData("Restricted", SecurityLevel.Restricted)]
    [InlineData("confidential", SecurityLevel.Confidential)] // case-insensitive
    [InlineData("totally-unknown", SecurityLevel.Restricted)] // fail-closed
    public void FromMetadata_maps_strings(string value, SecurityLevel expected)
        => Assert.Equal(expected, SecurityLevels.FromMetadata(value));
}
