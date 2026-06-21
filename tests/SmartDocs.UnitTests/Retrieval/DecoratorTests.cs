using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.Decorators;

namespace SmartDocs.UnitTests.Retrieval;

public sealed class DecoratorTests
{
    private static DocumentChunk Chunk(string id, string text)
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk(id + "#0", id, 0, text, 0, text.Length, meta);
    }

    [Fact]
    public async Task HydeRetriever_searches_with_LLM_generated_passage()
    {
        var captured = "";
        var inner = new RecordingRetriever(q =>
        {
            captured = q; return new[] {
            new RetrievalResult(Chunk("a", "alpha"), 0.9)
        };
        });
        var chat = new StubChatClient(_ => "Vacation policy hypothetical: 20 days per year accrued monthly.");
        var hyde = new HydeRetriever(inner, chat);

        await hyde.RetrieveAsync("vacation?", topK: 5);

        Assert.Contains("Vacation policy hypothetical", captured, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RagFusionRetriever_runs_inner_per_variant_and_merges()
    {
        var calls = new List<string>();
        var inner = new RecordingRetriever(q =>
        {
            calls.Add(q);
            return new[] { new RetrievalResult(Chunk(q, q), 0.5) };
        });
        var chat = new StubChatClient(_ => "Variant A\nVariant B");
        var fusion = new RagFusionRetriever(inner, chat, queryVariants: 2);

        var hits = await fusion.RetrieveAsync("Original Q", topK: 5);

        Assert.NotEmpty(hits);
        // Original + 2 variants = 3 inner calls.
        Assert.Equal(3, calls.Count);
        Assert.Contains("Original Q", calls);
        Assert.Contains("Variant A", calls);
    }

    [Fact]
    public async Task CragRetriever_drops_INCORRECT_results_and_keeps_CORRECT_ones()
    {
        var inner = new RecordingRetriever(_ => new[] {
            new RetrievalResult(Chunk("good", "20 vacation days"), 0.9),
            new RetrievalResult(Chunk("bad",  "completely unrelated"), 0.8),
        });
        var chat = new StubChatClient(p =>
            p.Contains("Passage: 20 vacation days", StringComparison.Ordinal) ? "CORRECT"
            : p.Contains("Passage: completely unrelated", StringComparison.Ordinal) ? "INCORRECT"
            : "AMBIGUOUS");

        var crag = new CragRetriever(inner, chat);
        var hits = await crag.RetrieveAsync("vacation?", topK: 5);

        Assert.Single(hits);
        Assert.Equal("good", hits[0].Chunk.DocumentId);
    }

    [Fact]
    public async Task CragRetriever_falls_back_to_web_when_all_INCORRECT()
    {
        var inner = new RecordingRetriever(_ => new[] {
            new RetrievalResult(Chunk("bad", "irrelevant"), 0.5),
        });
        var web = new RecordingRetriever(_ => new[] {
            new RetrievalResult(Chunk("web", "from the web"), 0.9),
        });
        var chat = new StubChatClient(_ => "INCORRECT");

        var crag = new CragRetriever(inner, chat) { WebFallback = web };
        var hits = await crag.RetrieveAsync("anything", topK: 3);

        Assert.Single(hits);
        Assert.Equal("web", hits[0].Chunk.DocumentId);
    }

    [Fact]
    public async Task StepBackRetriever_issues_original_and_stepback_queries_and_merges()
    {
        var calls = new List<string>();
        var inner = new RecordingRetriever(q =>
        {
            calls.Add(q);
            // Each leg returns a distinct hit so the RRF merge yields both.
            return new[] { new RetrievalResult(Chunk(q, q), 0.5) };
        });
        var chat = new StubChatClient(_ => "What are the general principles of employee leave?");
        var stepback = new StepBackRetriever(inner, chat);

        var hits = await stepback.RetrieveAsync("How many vacation days do I get?", topK: 5);

        // BOTH the original query and the broader step-back query were issued.
        Assert.Contains("How many vacation days do I get?", calls);
        Assert.Contains("What are the general principles of employee leave?", calls);
        Assert.Equal(2, calls.Count);

        // The two single-hit legs were fused into the result set.
        Assert.Equal(2, hits.Count);
        Assert.Equal("stepback(stub)", stepback.Strategy);
    }

    [Fact]
    public async Task StepBackRetriever_falls_back_to_original_when_stepback_is_empty()
    {
        var calls = new List<string>();
        var inner = new RecordingRetriever(q =>
        {
            calls.Add(q);
            return new[] { new RetrievalResult(Chunk(q, q), 0.5) };
        });
        var chat = new StubChatClient(_ => "   "); // model returned nothing usable
        var stepback = new StepBackRetriever(inner, chat);

        var hits = await stepback.RetrieveAsync("vacation?", topK: 5);

        // Only the original leg ran; no degenerate second query.
        Assert.Single(calls);
        Assert.Equal("vacation?", calls[0]);
        Assert.Single(hits);
    }

    private sealed class RecordingRetriever : IRetriever
    {
        private readonly Func<string, IReadOnlyList<RetrievalResult>> _impl;
        public RecordingRetriever(Func<string, IReadOnlyList<RetrievalResult>> impl) { _impl = impl; }
        public string Strategy => "stub";
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string q, int k, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([.. _impl(q).Take(k)]);
    }
}
