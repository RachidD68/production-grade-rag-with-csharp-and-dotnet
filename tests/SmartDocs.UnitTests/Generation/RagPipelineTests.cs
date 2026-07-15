using Microsoft.Extensions.AI;
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

    [Fact]
    public async Task AskStreamingAsync_emits_error_event_when_generation_faults()
    {
        var retriever = new StubRetriever([
            new RetrievalResult(Chunk("a", "alpha"), 0.9),
        ]);
        var promptEngine = new PromptTemplateEngine(new TokenCounter());
        // Yields one token, then throws mid-stream.
        var chat = new ThrowingChatClient(tokensBeforeThrow: 1);
        var pipeline = new RagPipeline(retriever, promptEngine, chat);

        var events = new List<RagStreamEvent>();
        await foreach (var ev in pipeline.AskStreamingAsync("what is alpha?"))
        {
            events.Add(ev);
        }

        // Sources came first; the sequence ends on Error and there is no Done after it.
        Assert.Equal(RagStreamEventKind.Sources, events[0].Kind);
        Assert.Equal(RagStreamEventKind.Error, events[^1].Kind);
        Assert.DoesNotContain(events, e => e.Kind == RagStreamEventKind.Done);
    }

    [Fact]
    public async Task AskStreamingAsync_emits_error_event_when_retrieval_faults()
    {
        var retriever = new ThrowingRetriever();
        var promptEngine = new PromptTemplateEngine(new TokenCounter());
        var chat = new StubChatClient(_ => "unreachable");
        var pipeline = new RagPipeline(retriever, promptEngine, chat);

        var events = new List<RagStreamEvent>();
        await foreach (var ev in pipeline.AskStreamingAsync("what is alpha?"))
        {
            events.Add(ev);
        }

        // A retrieval fault is terminal before any Sources: the only event is Error.
        var only = Assert.Single(events);
        Assert.Equal(RagStreamEventKind.Error, only.Kind);
        Assert.DoesNotContain(events, e => e.Kind == RagStreamEventKind.Sources);
        Assert.DoesNotContain(events, e => e.Kind == RagStreamEventKind.Done);
    }

    [Fact]
    public async Task AskStreamingAsync_propagates_cancellation_without_error_event()
    {
        var retriever = new StubRetriever([
            new RetrievalResult(Chunk("a", "alpha"), 0.9),
        ]);
        var promptEngine = new PromptTemplateEngine(new TokenCounter());
        // Cancels the token while streaming so the OCE originates mid-pipeline.
        var cts = new CancellationTokenSource();
        var chat = new ThrowingChatClient(tokensBeforeThrow: 0, cancelOnStream: cts);
        var pipeline = new RagPipeline(retriever, promptEngine, chat);

        // Cancellation must NOT be swallowed into an Error event — it propagates,
        // which is what lets the server stop billing when the browser disconnects.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var ev in pipeline.AskStreamingAsync("what is alpha?", cts.Token))
            {
                // drain
            }
        });
    }

    private sealed class StubRetriever : IRetriever
    {
        private readonly IReadOnlyList<RetrievalResult> _hits;
        public StubRetriever(IReadOnlyList<RetrievalResult> hits) { _hits = hits; }
        public string Strategy => "stub";
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string q, int topK, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([.. _hits.Take(topK)]);
    }

    private sealed class ThrowingRetriever : IRetriever
    {
        public string Strategy => "throwing";
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string q, int topK, CancellationToken ct = default)
            => throw new InvalidOperationException("retrieval backend is down");
    }

    /// <summary>
    /// <see cref="IChatClient"/> whose streaming response yields a fixed number of
    /// tokens and then faults. If a <see cref="CancellationTokenSource"/> is given,
    /// it is canceled instead of throwing, so the fault surfaces as an
    /// <see cref="OperationCanceledException"/> via the pipeline's token check.
    /// </summary>
    private sealed class ThrowingChatClient : IChatClient
    {
        private readonly int _tokensBeforeThrow;
        private readonly CancellationTokenSource? _cancelOnStream;

        public ThrowingChatClient(int tokensBeforeThrow, CancellationTokenSource? cancelOnStream = null)
        {
            _tokensBeforeThrow = tokensBeforeThrow;
            _cancelOnStream = cancelOnStream;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("non-streaming path not used in these tests");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => EnumerateAsync();

        private async IAsyncEnumerable<ChatResponseUpdate> EnumerateAsync()
        {
            for (var i = 0; i < _tokensBeforeThrow; i++)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, $"tok{i} ");
            }

            await Task.Yield();
            if (_cancelOnStream is not null)
            {
                // Trip the pipeline's ThrowIfCancellationRequested so an OCE
                // propagates rather than becoming an Error event.
                _cancelOnStream.Cancel();
                yield return new ChatResponseUpdate(ChatRole.Assistant, "after-cancel");
            }
            else
            {
                throw new InvalidOperationException("generation backend faulted mid-stream");
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}
