using SmartDocs.Retrieval.Vectorless;

namespace SmartDocs.UnitTests.Retrieval;

public sealed class VectorlessTests
{
    [Fact]
    public void DocumentStructureParser_builds_a_nested_tree_from_markdown()
    {
        var md =
            "# Top\n" +
            "Top body.\n" +
            "## Section 1\n" +
            "Body 1.\n" +
            "## Section 2\n" +
            "Body 2.\n" +
            "### Subsection 2.1\n" +
            "Body 2.1.\n";


        var index = DocumentStructureParser.Parse("Doc", md);

        var paths = index.Root.AllNodes().Select(n => n.Path).ToArray();
        Assert.Contains("/Doc", paths);
        Assert.Contains("/Doc/Top", paths);
        Assert.Contains("/Doc/Top/Section 1", paths);
        Assert.Contains("/Doc/Top/Section 2/Subsection 2.1", paths);
    }

    [Fact]
    public async Task VectorlessRetriever_returns_section_picked_by_LLM()
    {
        var md =
            "# Contract\n" +
            "## Article 1 — Definitions\n" +
            "## Article 2 — Obligations\n" +
            "Contoso shall deliver on time.\n" +
            "## Article 3 — Termination\n" +
            "Either party may terminate on 60 days notice.\n";
        var index = DocumentStructureParser.Parse("Contract", md);

        // Stub picks the index of the Termination node (we count nodes via AllNodes()).
        // Order: 0=Doc(Contract title) ... but the parser adds Title as root and # heading as child.
        // We ask for any single integer to keep the test stable; LLM picks "4".
        var chat = new StubChatClient(_ => "4");
        var retriever = new VectorlessRetriever(index, chat);

        var hits = await retriever.RetrieveAsync("How can the contract be terminated?", topK: 1);

        Assert.Single(hits);
        Assert.Contains("Article 3", hits[0].Chunk.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StructuralVectorRetriever_falls_back_when_vectorless_is_empty()
    {
        var index = DocumentStructureParser.Parse("Doc", "# H\n");
        var chat = new StubChatClient(_ => "");
        var vectorless = new VectorlessRetriever(index, chat);

        var fallbackHit = new SmartDocs.Core.Documents.RetrievalResult(
            new SmartDocs.Core.Documents.DocumentChunk(
                "f#0", "f", 0, "fallback", 0, 8,
                new SmartDocs.Core.Documents.DocumentMetadata(
                    "f", "x", "x", "x", "Internal", "Policy",
                    2026, "x", new DateOnly(2026, 1, 1), "x")),
            0.5);
        var fallback = new ConstantRetriever([fallbackHit]);
        var hybrid = new StructuralVectorRetriever(vectorless, fallback);

        var hits = await hybrid.RetrieveAsync("anything", topK: 5);

        Assert.Single(hits);
        Assert.Equal("fallback", hits[0].Chunk.Text);
    }

    private sealed class ConstantRetriever : SmartDocs.Core.Abstractions.IRetriever
    {
        private readonly IReadOnlyList<SmartDocs.Core.Documents.RetrievalResult> _r;
        public ConstantRetriever(IReadOnlyList<SmartDocs.Core.Documents.RetrievalResult> r) { _r = r; }
        public string Strategy => "stub";
        public Task<IReadOnlyList<SmartDocs.Core.Documents.RetrievalResult>> RetrieveAsync(string q, int k, CancellationToken ct = default)
            => Task.FromResult(_r);
    }
}
