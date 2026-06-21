using SmartDocs.Core.Documents;
using SmartDocs.Core.Filtering;
using SmartDocs.Retrieval.Filtering;

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

    [Fact]
    public void Or_combines_predicates()
    {
        var f1 = MetadataFilter.Where(m => m.Office == "Paris");
        var f2 = MetadataFilter.Where(m => m.Office == "Montreal");
        var either = f1.Or(f2);

        Assert.True(either.Matches(Meta(office: "Paris")));
        Assert.True(either.Matches(Meta(office: "Montreal")));
        Assert.False(either.Matches(Meta(office: "Casablanca")));
    }

    // ----- C2: extended Qdrant compiler operators ------------------------

    [Fact]
    public void Qdrant_compiler_emits_range_for_gte_and_lte()
    {
        var filterGte = MetadataFilter.Where(m => m.FiscalYear >= 2024);
        var jsonGte = QdrantFilterCompiler.Compile(filterGte);
        Assert.Contains("\"key\":\"fiscal_year\"", jsonGte, StringComparison.Ordinal);
        Assert.Contains("\"range\":", jsonGte, StringComparison.Ordinal);
        Assert.Contains("\"gte\":2024", jsonGte, StringComparison.Ordinal);

        var filterLte = MetadataFilter.Where(m => m.FiscalYear <= 2026);
        var jsonLte = QdrantFilterCompiler.Compile(filterLte);
        Assert.Contains("\"lte\":2026", jsonLte, StringComparison.Ordinal);
    }

    [Fact]
    public void Qdrant_compiler_emits_must_not_for_not_equal()
    {
        var filter = MetadataFilter.Where(m => m.Department != "Legal");
        var json = QdrantFilterCompiler.Compile(filter);

        Assert.Contains("\"must_not\":", json, StringComparison.Ordinal);
        Assert.Contains("\"key\":\"department\"", json, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"Legal\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Qdrant_compiler_emits_should_for_or()
    {
        var filter = MetadataFilter.Where(m => m.Office == "Paris" || m.Office == "Montreal");
        var json = QdrantFilterCompiler.Compile(filter);

        Assert.Contains("\"should\":", json, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"Paris\"", json, StringComparison.Ordinal);
        Assert.Contains("\"value\":\"Montreal\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Qdrant_compiler_emits_match_any_for_contains()
    {
        var offices = new[] { "Paris", "Montreal" };
        var filter = MetadataFilter.Where(m => offices.Contains(m.Office));
        var json = QdrantFilterCompiler.Compile(filter);

        Assert.Contains("\"key\":\"office\"", json, StringComparison.Ordinal);
        Assert.Contains("\"any\":", json, StringComparison.Ordinal);
        Assert.Contains("Paris", json, StringComparison.Ordinal);
        Assert.Contains("Montreal", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Qdrant_compiler_emits_is_empty_for_null_check()
    {
        var filter = MetadataFilter.Where(m => m.Author == null);
        var json = QdrantFilterCompiler.Compile(filter);

        Assert.Contains("\"is_empty\":", json, StringComparison.Ordinal);
        Assert.Contains("\"key\":\"author\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Qdrant_compiler_emits_must_not_is_empty_for_not_null()
    {
        var filter = MetadataFilter.Where(m => m.Author != null);
        var json = QdrantFilterCompiler.Compile(filter);

        Assert.Contains("\"must_not\":", json, StringComparison.Ordinal);
        Assert.Contains("\"is_empty\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Qdrant_compiler_throws_only_on_genuinely_unsupported_node()
    {
        // String.StartsWith is not a node the compiler models.
        var filter = MetadataFilter.Where(m => m.Title.StartsWith("Q3", StringComparison.Ordinal));
        Assert.Throws<NotSupportedException>(() => QdrantFilterCompiler.Compile(filter));
    }

    [Fact]
    public void Qdrant_compiler_builds_grpc_filter_for_supported_operators()
    {
        var filter = MetadataFilter.Where(m => m.Department == "HR" && m.FiscalYear >= 2024);
        var grpc = QdrantFilterCompiler.ToGrpcFilter(filter);

        // One must-condition is the nested AND sub-filter (HR + range).
        Assert.NotEmpty(grpc.Must);
    }
}
