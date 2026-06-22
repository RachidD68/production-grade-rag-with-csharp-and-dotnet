using System.Text;
using System.Text.RegularExpressions;
using SmartDocs.Security.Abstractions;

namespace SmartDocs.Security.Output;

/// <summary>
/// Offline last-mile PII redactor. Scrubs email addresses, US Social Security
/// numbers, and valid credit-card numbers out of a generated answer, replacing
/// each with a typed placeholder. Credit-card candidates are confirmed with the
/// Luhn checksum before redaction, so an arbitrary 16-digit number (an order id,
/// say) is not blindly masked.
/// </summary>
public sealed partial class OutputRedactor : IOutputRedactor
{
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex SsnRegex();

    // Credit-card candidate: 13–19 digits, optionally grouped by single spaces
    // or dashes. Validated with Luhn before it is treated as a real card.
    [GeneratedRegex(@"\b(?:\d[ -]?){12,18}\d\b", RegexOptions.Compiled)]
    private static partial Regex CreditCardCandidateRegex();

    /// <summary>
    /// Synchronous, allocation-light redaction. This is the chapter-accurate
    /// entry point; <see cref="RedactAsync"/> simply wraps it. Exposed as an
    /// instance method so it can substitute for the <see cref="IOutputRedactor"/>
    /// path without callers reaching for a static helper.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Public instance API per the chapter; mirrors RedactAsync and keeps the redactor swappable.")]
    public string Redact(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var result = EmailRegex().Replace(answer, "[REDACTED-EMAIL]");
        result = SsnRegex().Replace(result, "[REDACTED-SSN]");
        result = CreditCardCandidateRegex().Replace(result, MaskCardIfLuhnValid);
        return result;
    }

    public ValueTask<string> RedactAsync(string answer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<string>(Redact(answer));
    }

    private static string MaskCardIfLuhnValid(Match match)
        => IsLuhnValid(match.Value) ? "[REDACTED-CC]" : match.Value;

    /// <summary>
    /// Luhn (mod-10) checksum. Ignores embedded spaces/dashes; requires 13–19
    /// digits. Returns true only when the digits form a valid card number.
    /// </summary>
    private static bool IsLuhnValid(string candidate)
    {
        Span<byte> digits = stackalloc byte[19];
        var count = 0;
        foreach (var ch in candidate)
        {
            if (char.IsAsciiDigit(ch))
            {
                if (count == digits.Length)
                {
                    return false; // too long to be a card
                }
                digits[count++] = (byte)(ch - '0');
            }
            else if (ch is not (' ' or '-'))
            {
                return false;
            }
        }

        if (count is < 13 or > 19)
        {
            return false;
        }

        var sum = 0;
        var doubleDigit = false;
        for (var i = count - 1; i >= 0; i--)
        {
            var d = digits[i];
            if (doubleDigit)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }
            sum += d;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }
}
