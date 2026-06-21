using SmartDocs.Routing.Filtering;

namespace SmartDocs.UnitTests.Routing;

/// <summary>C3: vocabulary validation drops out-of-corpus values.</summary>
public sealed class FilterVocabularyTests
{
    [Fact]
    public void Clean_drops_out_of_vocabulary_department_but_keeps_valid_values()
    {
        var raw = new ExtractedFilter
        {
            Department = "Marketing", // not in the corpus vocabulary
            Office = "Paris",         // valid
            FiscalYear = 2026,        // valid
            FreeTextQuery = "remote work",
        };

        var cleaned = FilterVocabulary.Clean(raw);

        Assert.Null(cleaned.Department); // bogus constraint dropped, not applied
        Assert.Equal("Paris", cleaned.Office);
        Assert.Equal(2026, cleaned.FiscalYear);
        Assert.Equal("remote work", cleaned.FreeTextQuery);
    }

    [Fact]
    public void Clean_is_case_sensitive_to_canonical_casing()
    {
        var raw = new ExtractedFilter { Department = "hr", Office = "paris" };

        var cleaned = FilterVocabulary.Clean(raw);

        Assert.Null(cleaned.Department); // "hr" != canonical "HR"
        Assert.Null(cleaned.Office);     // "paris" != canonical "Paris"
    }

    [Fact]
    public void Clean_keeps_every_canonical_value()
    {
        var raw = new ExtractedFilter
        {
            Department = "Engineering",
            Office = "Casablanca",
            Silo = "technical-docs",
            DocumentType = "ADR",
            FiscalYear = 2025,
        };

        var cleaned = FilterVocabulary.Clean(raw);

        Assert.Equal("Engineering", cleaned.Department);
        Assert.Equal("Casablanca", cleaned.Office);
        Assert.Equal("technical-docs", cleaned.Silo);
        Assert.Equal("ADR", cleaned.DocumentType);
        Assert.Equal(2025, cleaned.FiscalYear);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    [InlineData(0)]
    public void Clean_drops_fiscal_year_outside_sane_range(int year)
    {
        var cleaned = FilterVocabulary.Clean(new ExtractedFilter { FiscalYear = year });
        Assert.Null(cleaned.FiscalYear);
    }

    [Fact]
    public void Clean_drops_bogus_silo_and_document_type()
    {
        var raw = new ExtractedFilter { Silo = "marketing-collateral", DocumentType = "Memo" };
        var cleaned = FilterVocabulary.Clean(raw);

        Assert.Null(cleaned.Silo);
        Assert.Null(cleaned.DocumentType);
    }
}
