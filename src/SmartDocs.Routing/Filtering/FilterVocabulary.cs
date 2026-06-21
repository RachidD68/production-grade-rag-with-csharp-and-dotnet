using System.Collections.Frozen;

namespace SmartDocs.Routing.Filtering;

/// <summary>
/// The closed vocabularies for the structured filter fields, derived from the
/// <c>DocumentMetadata</c> XML-doc schema. An LLM extracting a filter can
/// hallucinate a value that is plausible but not in the corpus (e.g. a
/// <c>Department</c> of <c>"Marketing"</c>). Applying such a constraint would
/// silently empty the result set — strictly worse than ignoring it. <see cref="Clean"/>
/// nulls out-of-vocabulary values so a bogus constraint is simply dropped.
/// </summary>
public static class FilterVocabulary
{
    /// <summary>Allowed <c>Department</c> values (canonical casing).</summary>
    public static readonly FrozenSet<string> Departments =
        new[] { "HR", "Engineering", "Finance", "Legal", "Product" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Allowed <c>Office</c> values (canonical casing).</summary>
    public static readonly FrozenSet<string> Offices =
        new[] { "Montreal", "Paris", "Casablanca" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Allowed <c>Silo</c> values (canonical casing).</summary>
    public static readonly FrozenSet<string> Silos = new[]
    {
        "hr-policies", "technical-docs", "financial-reports",
        "legal-contracts", "product-catalog", "release-notes-tickets",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Allowed <c>DocumentType</c> values (canonical casing).</summary>
    public static readonly FrozenSet<string> DocumentTypes = new[]
    {
        "Policy", "ADR", "Runbook", "Reference", "Report",
        "Contract", "Specification", "ReleaseNote", "Ticket",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Plausible lower/upper bounds for <c>FiscalYear</c>; anything outside is treated as noise.</summary>
    public const int MinFiscalYear = 2000;

    /// <inheritdoc cref="MinFiscalYear"/>
    public const int MaxFiscalYear = 2100;

    /// <summary>
    /// Returns a copy of <paramref name="raw"/> with every out-of-vocabulary
    /// value nulled (case-sensitive match to the canonical casing). A
    /// <c>FiscalYear</c> outside the sane range is also dropped. The
    /// <c>FreeTextQuery</c> is passed through unchanged.
    /// </summary>
    public static ExtractedFilter Clean(ExtractedFilter raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return raw with
        {
            Department = Keep(raw.Department, Departments),
            Office = Keep(raw.Office, Offices),
            Silo = Keep(raw.Silo, Silos),
            DocumentType = Keep(raw.DocumentType, DocumentTypes),
            FiscalYear = raw.FiscalYear is { } y && y is >= MinFiscalYear and <= MaxFiscalYear
                ? raw.FiscalYear
                : null,
        };
    }

    private static string? Keep(string? value, FrozenSet<string> allowed)
        => value is not null && allowed.Contains(value) ? value : null;
}
