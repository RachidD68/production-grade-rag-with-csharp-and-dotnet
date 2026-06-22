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
    public async Task MultiAgentWorkflow_Sequential_runs_researcher_analyst_checker_writer()
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
        var answer = await workflow.RunSequentialAsync("What is alpha?");

        Assert.Equal("Final polished answer with [Source 1].", answer);
    }

    [Fact]
    public async Task MultiAgentWorkflow_AgentsAsTools_returns_manager_decided_answer()
    {
        // The agents-as-tools pattern hands orchestration to the Manager agent —
        // it decides which workers (if any) to invoke via tool calls. The stub
        // chat client doesn't emit tool calls, so the Manager simply returns
        // its first response. This test verifies the orchestration plumbing
        // (session creation, tool wiring, manager construction) is sound;
        // tool-call routing is exercised in integration tests against a real
        // LLM.
        var chat = new StubChatClient(_ =>
            "Final answer chosen by the Manager [Source 1].");
        var retriever = new ConstantRetriever([
            new RetrievalResult(Chunk("a", "alpha"), 0.9),
        ]);

        var workflow = new MultiAgentWorkflow(chat, retriever);
        var answer = await workflow.RunAgentsAsToolsAsync("What is alpha?");

        Assert.False(string.IsNullOrWhiteSpace(answer));
        Assert.Contains("[Source 1]", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmartDocsAgent_RunWithInjectedChunks_pre_loads_context()
    {
        // Verify that pre-injection passes retrieved chunks to the agent as
        // a preamble, so the agent's first turn starts with grounded context
        // (no tool call required). The stub asserts on the pre-injection
        // prefix and answers accordingly.
        var chat = new StubChatClient(prompt =>
            prompt.Contains("Pre-retrieved context", StringComparison.Ordinal)
                ? "Answer drawn from preamble [Source 1]."
                : "I would need to search.");
        var retriever = new ConstantRetriever([
            new RetrievalResult(Chunk("a", "alpha is the first letter"), 0.95),
        ]);

        var agent = SmartDocsAgent.Create(chat, retriever);
        var answer = await SmartDocsAgent.RunWithInjectedChunksAsync(
            agent, "What is alpha?", new ChunkInjectorOptions(retriever, TopK: 1));

        Assert.Equal("Answer drawn from preamble [Source 1].", answer);
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
