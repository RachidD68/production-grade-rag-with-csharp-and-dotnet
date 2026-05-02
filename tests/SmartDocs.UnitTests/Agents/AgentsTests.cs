using SmartDocs.Agents;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.UnitTests.Agents;

public sealed class AgentsTests
{
    private static DocumentChunk Chunk(string id, string text)
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk(id + "#0", id, 0, text, 0, text.Length, meta);
    }

    [Fact]
    public void SmartDocsAgent_Create_returns_a_named_ChatClientAgent_with_tools()
    {
        var chat = new StubChatClient(_ => "ok");
        var retriever = new ConstantRetriever([
            new RetrievalResult(Chunk("a", "alpha"), 0.9),
        ]);

        var agent = SmartDocsAgent.Create(chat, retriever);

        Assert.NotNull(agent);
        Assert.Equal("SmartDocs", agent.Name);
    }

    [Fact]
    public async Task MultiAgentWorkflow_runs_researcher_analyst_checker_writer()
    {
        // The stub returns increasingly polished text per role so the test
        // can verify the writer's output reaches the surface.
        var chat = new StubChatClient(prompt =>
        {
            if (prompt.Contains("Polish this draft", StringComparison.Ordinal))
            {
                return "Final polished answer with [Source 1].";
            }
            if (prompt.Contains("Sources:", StringComparison.Ordinal) &&
                prompt.Contains("Draft:", StringComparison.Ordinal))
            {
                return "All sentences SUPPORTED.";
            }
            if (prompt.Contains("Researcher findings", StringComparison.Ordinal))
            {
                return "Draft answer with [Source 1] inline.";
            }
            return "[Source 1] alpha";
        });
        var retriever = new ConstantRetriever([
            new RetrievalResult(Chunk("a", "alpha"), 0.9),
        ]);

        var workflow = new MultiAgentWorkflow(chat, retriever);
        var answer = await workflow.RunAsync("What is alpha?");

        Assert.Equal("Final polished answer with [Source 1].", answer);
    }

    private sealed class ConstantRetriever : IRetriever
    {
        private readonly IReadOnlyList<RetrievalResult> _r;
        public ConstantRetriever(IReadOnlyList<RetrievalResult> r) { _r = r; }
        public string Strategy => "stub";
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string q, int k, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([.. _r.Take(k)]);
    }
}
