using SmartDocs.Routing.Filtering;

namespace SmartDocs.UnitTests.Routing;

public sealed class QueryConstructorTests
{
    [Fact]
    public async Task Extracts_department_office_and_free_text_from_LLM_json()
    {
        var stub = new StubChatClient(_ =>
            "{\"department\":\"HR\",\"office\":\"Paris\",\"freeTextQuery\":\"vacation policy\"}");
        var qc = new QueryConstructor(stub);

        var extracted = await qc.ExtractAsync("Show me HR vacation policies for the Paris office");

        Assert.Equal("HR", extracted.Department);
        Assert.Equal("Paris", extracted.Office);
        Assert.Equal("vacation policy", extracted.FreeTextQuery);
    }

    [Fact]
    public async Task Tolerates_LLM_response_wrapped_in_markdown_fence()
    {
        var stub = new StubChatClient(_ =>
            "Here you go:\n```json\n{\"silo\":\"legal-contracts\"}\n```");
        var qc = new QueryConstructor(stub);

        var extracted = await qc.ExtractAsync("Show me Acme contracts");

        Assert.Equal("legal-contracts", extracted.Silo);
    }

    [Fact]
    public async Task Falls_back_to_free_text_only_on_unparseable_LLM_response()
    {
        var stub = new StubChatClient(_ => "I don't know what you mean");
        var qc = new QueryConstructor(stub);

        var extracted = await qc.ExtractAsync("foo");

        Assert.Equal("foo", extracted.FreeTextQuery);
    }

    [Fact]
    public void ToMetadataFilter_combines_all_present_constraints()
    {
        var extracted = new ExtractedFilter { Department = "Finance", FiscalYear = 2025 };
        var filter = QueryConstructor.ToMetadataFilter(extracted);

        var matchingMeta = new SmartDocs.Core.Documents.DocumentMetadata(
            "d", "financial-reports", "Finance", "Montreal", "Restricted", "Report",
            2025, "x", new DateOnly(2025, 6, 1), "Q3 Report");
        var nonMatchingMeta = matchingMeta with { Department = "HR" };

        Assert.True(filter.Matches(matchingMeta));
        Assert.False(filter.Matches(nonMatchingMeta));
    }
}
