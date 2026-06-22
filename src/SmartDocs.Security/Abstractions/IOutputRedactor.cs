namespace SmartDocs.Security.Abstractions;

/// <summary>
/// Last-mile redactor that scrubs personally identifiable information (PII) and
/// other sensitive shapes out of a generated answer before it reaches the user.
/// Implementations include a fully offline regex+Luhn redactor
/// (<c>OutputRedactor</c>) and a Presidio-backed one (<c>PresidioRedactor</c>).
/// </summary>
public interface IOutputRedactor
{
    /// <summary>Returns <paramref name="answer"/> with detected sensitive spans replaced by placeholders.</summary>
    ValueTask<string> RedactAsync(string answer, CancellationToken cancellationToken = default);
}
