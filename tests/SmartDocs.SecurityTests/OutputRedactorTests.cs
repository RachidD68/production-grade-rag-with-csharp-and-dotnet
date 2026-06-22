using SmartDocs.Security.Output;

namespace SmartDocs.SecurityTests;

public sealed class OutputRedactorTests
{
    private readonly OutputRedactor _redactor = new();

    [Fact]
    public void Email_is_redacted()
    {
        var result = _redactor.Redact("Reach me at jane.doe@contoso.com for details.");
        Assert.Contains("[REDACTED-EMAIL]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("jane.doe@contoso.com", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Ssn_is_redacted()
    {
        var result = _redactor.Redact("The applicant's SSN is 123-45-6789 on file.");
        Assert.Contains("[REDACTED-SSN]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("123-45-6789", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_luhn_card_is_redacted()
    {
        // 4111111111111111 is a well-known Luhn-valid test Visa number.
        var result = _redactor.Redact("Charge card 4111111111111111 please.");
        Assert.Contains("[REDACTED-CC]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("4111111111111111", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Spaced_valid_luhn_card_is_redacted()
    {
        var result = _redactor.Redact("Card: 4111 1111 1111 1111.");
        Assert.Contains("[REDACTED-CC]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_luhn_sixteen_digits_is_not_redacted()
    {
        // 1234567812345678 has 16 digits but FAILS the Luhn checksum — a false
        // positive a naive regex would mask. It must survive untouched.
        const string text = "Order reference 1234567812345678 shipped today.";
        var result = _redactor.Redact(text);
        Assert.Equal(text, result);
        Assert.DoesNotContain("[REDACTED-CC]", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedactAsync_matches_synchronous_redact()
    {
        const string text = "Email a@b.com and SSN 111-22-3333 and card 4111111111111111.";
        var viaAsync = await _redactor.RedactAsync(text, CancellationToken.None);
        Assert.Equal(_redactor.Redact(text), viaAsync);
    }
}
