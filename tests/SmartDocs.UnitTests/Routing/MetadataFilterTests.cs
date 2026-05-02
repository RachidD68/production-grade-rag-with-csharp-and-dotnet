using SmartDocs.Core.Documents;
using SmartDocs.Routing.Filtering;

namespace SmartDocs.UnitTests.Routing;

public sealed class MetadataFilterTests
{
    private static DocumentMetadata Meta(string office = "Montreal", int year = 2026, string dept = "HR") =>
        new("d", "hr-policies", dept, office, "Internal", "Policy",
            year, "x", new DateOnly(2026, 1, 1), "x");

    [Fact]
    public void Where_predicate_matches_metadata()
    {
        var filter = MetadataFilter.Where(m => m.Office == "Paris");
        Assert.False(filter.Matches(Meta()));
        Assert.True(filter.Matches(Meta(office: "Paris")));
    }

    [Fact]
    public void And_combines_predicates()
    {
        var f1 = MetadataFilter.Where(m => m.Department == "HR");
        var f2 = MetadataFilter.Where(m => m.FiscalYear == 2026);
        var combined = f1.And(f2);

        Assert.True(combined.Matches(Meta(year: 2026, dept: "HR")));
        Assert.False(combined.Matches(Meta(year: 2024, dept: "HR")));
        Assert.False(combined.Matches(Meta(year: 2026, dept: "Engineering")));
    }

    [Fact]
    public void All_matches_anything()
    {
        Assert.True(MetadataFilter.All.Matches(Meta()));
    }

    [Fact]
    public void Qdrant_compiler_emits_must_array_for_AndAlso_chain()
    {
        var filter = MetadataFilter.Where(m => m.Department == "HR" && m.Office == "Paris");
        var json = QdrantFilterCompiler.Compile(filter);

        Assert.Contains("\"must\":", json, StringComparison.Ordinal);
        Assert.Contains("\"key\":\"department\"", json, StringComparison.Ordinal);
        Assert.Contains("\"key\":\"office\"", json, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"HR\"", json, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"Paris\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Qdrant_compiler_handles_int_equality()
    {
        var year = 2026;
        var filter = MetadataFilter.Where(m => m.FiscalYear == year);
        var json = QdrantFilterCompiler.Compile(filter);

        Assert.Contains("\"key\":\"fiscal_year\"", json, StringComparison.Ordinal);
        Assert.Contains("\"value\":2026", json, StringComparison.Ordinal);
    }
}
