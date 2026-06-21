using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
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

        var paths = index.AllNodes().Select(n => n.Path).ToArray();
        Assert.Contains("/Doc", paths);
        Assert.Contains("/Doc/Top", paths);
        Assert.Contains("/Doc/Top/Section 1", paths);
        Assert.Contains("/Doc/Top/Section 2/Subsection 2.1", paths);
    }

    // --- Token-free deterministic tests (no LLM call) ----------------------

    [Fact]
    public void Lookup_returns_node_by_id()
    {
        var (index, _) = BuildSampleIndex();
        var retriever = new StructuralRetriever(index, new StubChatClient(_ => ""));

        var node = retriever.Lookup("Art17");

        Assert.NotNull(node);
        Assert.Equal("Article 17", node!.Title);
        Assert.Null(retriever.Lookup("does-not-exist"));
    }

    [Fact]
    public void WithCrossReferences_returns_cited_closure_at_depth_1()
    {
        var (index, _) = BuildSampleIndex();
        var retriever = new StructuralRetriever(index, new StubChatClient(_ => ""));

        // Art17 cites Art19; depth 1 returns both (seed + one hop), not Art21
        // (which is only reachable through Art19 at depth 2).
        var closure = retriever.WithCrossReferences("Art17", depth: 1);

        var ids = closure.Select(n => n.Id).ToArray();
        Assert.Equal("Art17", ids[0]);
        Assert.Contains("Art19", ids);
        Assert.DoesNotContain("Art21", ids);
    }

    [Fact]
    public void WithCrossReferences_is_bounded_by_depth()
    {
        var (index, _) = BuildSampleIndex();
        var retriever = new StructuralRetriever(index, new StubChatClient(_ => ""));

        var depth0 = retriever.WithCrossReferences("Art17", depth: 0);
        var depth1 = retriever.WithCrossReferences("Art17", depth: 1);
        var depth2 = retriever.WithCrossReferences("Art17", depth: 2);

        // depth 0 = seed only; each extra hop strictly widens (or holds) the closure.
        Assert.Equal(["Art17"], depth0.Select(n => n.Id));
        Assert.Contains("Art19", depth1.Select(n => n.Id));
        Assert.DoesNotContain("Art21", depth1.Select(n => n.Id));
        Assert.Contains("Art21", depth2.Select(n => n.Id));
        Assert.True(depth2.Count >= depth1.Count);
    }

    [Fact]
    public void Ancestors_Descendants_Siblings_walk_the_tree()
    {
        var (index, _) = BuildSampleIndex();
        var retriever = new StructuralRetriever(index, new StubChatClient(_ => ""));

        // Tree: GDPR (root) -> { Art17, Art19, Art21 }.
        var ancestors = retriever.Ancestors("Art17").Select(n => n.Id).ToArray();
        Assert.Equal(["GDPR"], ancestors);

        var descendants = retriever.Descendants("GDPR").Select(n => n.Id).ToArray();
        Assert.Contains("Art17", descendants);
        Assert.Contains("Art19", descendants);
        Assert.Contains("Art21", descendants);

        var siblings = retriever.Siblings("Art17").Select(n => n.Id).ToArray();
        Assert.Contains("Art19", siblings);
        Assert.Contains("Art21", siblings);
        Assert.DoesNotContain("Art17", siblings);
    }

    [Theory]
    [InlineData("structural", "structural-hit")]
    [InlineData("vector", "vector-hit")]
    [InlineData("both", "fused-hit")]
    [InlineData("unknown-route", "vector-hit")] // default branch
    public async Task HybridRouter_routes_structural_vector_both(string destination, string expectedText)
    {
        var router = new SmartDocs.Routing.HybridRouter(
            new FakeClassifier(destination),
            structural: new ConstantRetriever([Hit("structural-hit")]),
            vector: new ConstantRetriever([Hit("vector-hit")]),
            fused: new ConstantRetriever([Hit("fused-hit")]));

        var hits = await router.RetrieveAsync("any query", topK: 5);

        Assert.Single(hits);
        Assert.Equal(expectedText, hits[0].Chunk.Text);
    }

    [Fact]
    public async Task KeywordRouteClassifier_classifies_identifier_topic_and_mix()
    {
        var classifier = new SmartDocs.Routing.KeywordRouteClassifier();

        Assert.Equal("structural", (await classifier.ClassifyAsync("Article 17")).Destination);
        Assert.Equal("vector", (await classifier.ClassifyAsync("how do I erase my personal data")).Destination);
        Assert.Equal("both",
            (await classifier.ClassifyAsync("how does Article 17 interact with the right to portability")).Destination);
    }

    // --- The renamed LLM section router (uses the stub IChatClient) ---------

    [Fact]
    public async Task LlmSectionRouter_returns_section_picked_by_LLM()
    {
        var md =
            "# Contract\n" +
            "## Article 1 — Definitions\n" +
            "## Article 2 — Obligations\n" +
            "Contoso shall deliver on time.\n" +
            "## Article 3 — Termination\n" +
            "Either party may terminate on 60 days notice.\n";
        var index = DocumentStructureParser.Parse("Contract", md);

        // The stub picks the index of the Termination node (counted via AllNodes()).
        var chat = new StubChatClient(_ => "4");
        var router = new LlmSectionRouter(index, chat);

        var hits = await router.RetrieveAsync("How can the contract be terminated?", topK: 1);

        Assert.Single(hits);
        Assert.Contains("Article 3", hits[0].Chunk.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StructuralRetriever_falls_back_to_topic_when_no_identifier()
    {
        var (index, _) = BuildSampleIndex();
        // Stub returns no explicit id, so the retriever delegates to the fallback.
        var chat = new StubChatClient(_ => "{ \"explicitId\": null, \"topic\": \"erasure\" }");
        var fallback = new ConstantRetriever([Hit("fallback")]);
        var retriever = new StructuralRetriever(index, chat, fallback);

        var hits = await retriever.RetrieveAsync("tell me about erasure", topK: 5);

        Assert.Single(hits);
        Assert.Equal("fallback", hits[0].Chunk.Text);
    }

    // --- Helpers -----------------------------------------------------------

    // Builds a small id-linked index by hand:
    //   GDPR (root) -> Art17, Art19, Art21
    //   Art17 cites Art19; Art19 cites Art21 (a 2-hop reference chain).
    private static (StructuralIndex Index, IReadOnlyList<StructuralNode> Nodes) BuildSampleIndex()
    {
        var root = new StructuralNode(
            "GDPR", "/GDPR", "GDPR", "General Data Protection Regulation.",
            ParentId: null, ChildIds: ["Art17", "Art19", "Art21"], CrossReferences: []);
        var art17 = new StructuralNode(
            "Art17", "/GDPR/Article 17", "Article 17", "Right to erasure. See Article 19.",
            ParentId: "GDPR", ChildIds: [], CrossReferences: ["Art19"]);
        var art19 = new StructuralNode(
            "Art19", "/GDPR/Article 19", "Article 19", "Notification obligation. See Article 21.",
            ParentId: "GDPR", ChildIds: [], CrossReferences: ["Art21"]);
        var art21 = new StructuralNode(
            "Art21", "/GDPR/Article 21", "Article 21", "Right to object.",
            ParentId: "GDPR", ChildIds: [], CrossReferences: []);
        var nodes = new[] { root, art17, art19, art21 };
        return (new StructuralIndex(root, nodes), nodes);
    }

    private static RetrievalResult Hit(string text) =>
        new(
            new DocumentChunk(
                text, "doc", 0, text, 0, text.Length,
                new DocumentMetadata(
                    "doc", "x", "x", "x", "Internal", "Policy",
                    2026, "x", new DateOnly(2026, 1, 1), "x")),
            0.5);

    private sealed class FakeClassifier(string destination) : SmartDocs.Routing.IRouteClassifier
    {
        public Task<SmartDocs.Routing.RouteClassification> ClassifyAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SmartDocs.Routing.RouteClassification(destination));
    }

    private sealed class ConstantRetriever(IReadOnlyList<RetrievalResult> r) : IRetriever
    {
        public string Strategy => "stub";
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string q, int k, CancellationToken ct = default)
            => Task.FromResult(r);
    }
}
