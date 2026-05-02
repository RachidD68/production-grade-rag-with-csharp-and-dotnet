using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;
using SmartDocs.Generation;

namespace SmartDocs.UnitTests.Generation;

public sealed class RagPipelineTests
{
    private static DocumentChunk Chunk(string id, string text)
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk(id + "#0", id, 0, text, 0, text.Length, meta);
    }

    [Fact]
    public async Task AskAsync_returns_grounded_answer_with_citations()
    {
        var retriever = new StubRetriever([
            new RetrievalResult(Chunk("hr-vac", "20 paid vacation days per year."), 0.9),
            new RetrievalResult(Chunk("hr-sick", "Unlimited sick leave."), 0.4),
        ]);
        var promptEngine = new PromptTemplateEngine(new TokenCounter());
        var chat = new StubChatClient(_ => "20 days according to [Source 1].");
        var pipeline = new RagPipeline(retriever, promptEngine, chat);

        var response = await pipeline.AskAsync("How many vacation days?");

        Assert.Equal("20 days according to [Source 1].", response.Answer);
        Assert.Equal(2, response.Sources.Count);
        Assert.Equal("hr-vac#0", response.Sources[0].Chunk.ChunkId);
        Assert.True(response.LatencyMs >= 0);
    }

    [Fact]
    public async Task AskAsync_with_no_retrieval_results_falls_back_to_idk_prompt()
    {
        var retriever = new StubRetriever([]);
        var promptEngine = new PromptTemplateEngine(new TokenCounter());
        // The chat client just echoes the user prompt so we can verify the IDK fallback was used.
        var chat = new StubChatClient(p => p.Contains("I don't know based on", StringComparison.Ordinal)
            ? "I don't know based on the available sources."
            : "Should not happen.");

        var pipeline = new RagPipeline(retriever, promptEngine, chat);

        var response = await pipeline.AskAsync("What's our Q3 revenue?");

        Assert.Empty(response.Sources);
        Assert.Equal("I don't know based on the available sources.", response.Answer);
    }

    [Fact]
    public async Task AskStreamingAsync_yields_sources_then_tokens_then_done()
    {
        var retriever = new StubRetriever([
            new RetrievalResult(Chunk("a", "alpha"), 0.9),
        ]);
        var promptEngine = new PromptTemplateEngine(new TokenCounter());
        var chat = new StubChatClient(_ => "Alpha is the first letter.");
        var pipeline = new RagPipeline(retriever, promptEngine, chat);

        var events = new List<RagStreamEvent>();
        await foreach (var ev in pipeline.AskStreamingAsync("what is alpha?"))
        {
            events.Add(ev);
        }

        Assert.Equal(RagStreamEventKind.Sources, events[0].Kind);
        Assert.Equal(RagStreamEventKind.Done, events[^1].Kind);
        Assert.Contains(events, e => e.Kind == RagStreamEventKind.Token);
    }

    private sealed class StubRetriever : IRetriever
    {
        private readonly IReadOnlyList<RetrievalResult> _hits;
        public StubRetriever(IReadOnlyList<RetrievalResult> hits) { _hits = hits; }
        public string Strategy => "stub";
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string q, int topK, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([.. _hits.Take(topK)]);
    }
}
