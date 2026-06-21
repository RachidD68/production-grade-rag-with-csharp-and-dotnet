using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Reranking;

namespace SmartDocs.UnitTests.Reranking;

public sealed class RerankerTests
{
    private static DocumentChunk Chunk(string id, string text)
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk(id + "#0", id, 0, text, 0, text.Length, meta);
    }

    [Fact]
    public async Task NoOpReranker_preserves_order_and_truncates_to_topK()
    {
        var input = new[]
        {
            new RetrievalResult(Chunk("a","x"), 0.9),
            new RetrievalResult(Chunk("b","x"), 0.8),
            new RetrievalResult(Chunk("c","x"), 0.7),
        };
        var reranker = new NoOpReranker();
        var output = await reranker.RerankAsync("q", input, topK: 2);

        Assert.Equal(2, output.Count);
        Assert.Equal("a", output[0].Chunk.DocumentId);
    }

    [Fact]
    public async Task LlmRerank_resorts_by_LLM_relevance_score()
    {
        // The stub returns a different score per candidate based on text.
        var chat = new StubChatClient(prompt =>
        {
            if (prompt.Contains("Passage: about vacation policy", StringComparison.Ordinal))
            {
                return "0.95";
            }
            if (prompt.Contains("Passage: about remote work", StringComparison.Ordinal))
            {
                return "0.30";
            }
            return "0.10";
        });
        var reranker = new LlmRerank(chat);

        var input = new[]
        {
            new RetrievalResult(Chunk("remote", "about remote work"),       0.99),
            new RetrievalResult(Chunk("vac",    "about vacation policy"),   0.10),
            new RetrievalResult(Chunk("none",   "about something else"),    0.50),
        };

        var output = await reranker.RerankAsync("vacation days", input, topK: 3);

        Assert.Equal("vac", output[0].Chunk.DocumentId);
        Assert.Equal("remote", output[1].Chunk.DocumentId);
        Assert.Equal("none", output[2].Chunk.DocumentId);
    }

    [Fact]
    public async Task RerankingMiddleware_pulls_candidate_count_then_picks_topK()
    {
        var rawInput = Enumerable.Range(0, 20)
            .Select(i => new RetrievalResult(Chunk($"d{i}", $"text{i}"), 1.0 / (i + 1)))
            .ToList();
        var inner = new ConstantRetriever(rawInput);
        var reranker = new NoOpReranker();
        var middleware = new RerankingMiddleware(inner, reranker, candidateCount: 20);

        var output = await middleware.RetrieveAsync("query", topK: 5);

        Assert.Equal(5, output.Count);
        Assert.Equal(20, inner.LastTopK);
    }

    [Fact]
    public async Task NoOpReranker_returns_empty_for_empty_candidates()
    {
        var reranker = new NoOpReranker();
        var output = await reranker.RerankAsync("q", Array.Empty<RetrievalResult>(), topK: 5);
        Assert.Empty(output);
    }

    [Fact]
    public async Task OnnxCrossEncoderReranker_returns_empty_for_empty_candidates()
    {
        var reranker = new OnnxCrossEncoderReranker(new StubCrossEncoderModel(_ => 0.5f));
        var output = await reranker.RerankAsync("q", Array.Empty<RetrievalResult>(), topK: 5);
        Assert.Empty(output);
    }

    [Fact]
    public async Task RerankingMiddleware_minScore_floor_drops_below_threshold()
    {
        var input = new[]
        {
            new RetrievalResult(Chunk("hi",  "x"), 0.9),
            new RetrievalResult(Chunk("mid", "x"), 0.2),
            new RetrievalResult(Chunk("lo",  "x"), 0.05),
        };
        var inner = new ConstantRetriever(input);
        // A reranker that passes the candidate scores through unchanged.
        var reranker = new ScoreEchoReranker();
        var middleware = new RerankingMiddleware(inner, reranker, candidateCount: 20, minScore: 0.3);

        var output = await middleware.RetrieveAsync("q", topK: 5);

        Assert.Single(output);
        Assert.Equal("hi", output[0].Chunk.DocumentId);
    }

    [Fact]
    public async Task RerankingMiddleware_minScore_above_all_returns_empty()
    {
        var input = new[]
        {
            new RetrievalResult(Chunk("hi",  "x"), 0.9),
            new RetrievalResult(Chunk("mid", "x"), 0.2),
            new RetrievalResult(Chunk("lo",  "x"), 0.05),
        };
        var inner = new ConstantRetriever(input);
        var reranker = new ScoreEchoReranker();
        var middleware = new RerankingMiddleware(inner, reranker, candidateCount: 20, minScore: 0.95);

        var output = await middleware.RetrieveAsync("q", topK: 5);

        Assert.Empty(output);
    }

    [Fact]
    public async Task OnnxCrossEncoderReranker_resorts_by_model_score()
    {
        // Deterministic stub: score by the document text, ignoring the query.
        var model = new StubCrossEncoderModel(text => text switch
        {
            "about vacation policy" => 0.95f,
            "about remote work" => 0.30f,
            _ => 0.10f,
        });
        var reranker = new OnnxCrossEncoderReranker(model);

        var input = new[]
        {
            new RetrievalResult(Chunk("remote", "about remote work"),     0.99),
            new RetrievalResult(Chunk("vac",    "about vacation policy"), 0.10),
            new RetrievalResult(Chunk("none",   "about something else"),  0.50),
        };

        // topK truncation: ask for 2, expect the two highest model scores in order.
        var output = await reranker.RerankAsync("vacation days", input, topK: 2);

        Assert.Equal(2, output.Count);
        Assert.Equal("vac", output[0].Chunk.DocumentId);
        Assert.Equal("remote", output[1].Chunk.DocumentId);
        // Alignment: the surviving scores are the model's, mapped onto the right chunks.
        Assert.Equal(0.95f, output[0].Score, precision: 5);
        Assert.Equal(0.30f, output[1].Score, precision: 5);
    }

    private sealed class ConstantRetriever : IRetriever
    {
        private readonly IReadOnlyList<RetrievalResult> _results;
        public int LastTopK { get; private set; }
        public string Strategy => "constant";
        public ConstantRetriever(IReadOnlyList<RetrievalResult> results) { _results = results; }
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string query, int topK, CancellationToken ct = default)
        {
            LastTopK = topK;
            IReadOnlyList<RetrievalResult> r = _results.Take(topK).ToList();
            return Task.FromResult(r);
        }
    }

    // A reranker that echoes each candidate's incoming score, only truncating to
    // topK — lets a test drive RerankingMiddleware with known scores.
    private sealed class ScoreEchoReranker : IReranker
    {
        public string Implementation => "score-echo";
        public Task<IReadOnlyList<RetrievalResult>> RerankAsync(
            string query, IReadOnlyList<RetrievalResult> candidates, int topK, CancellationToken ct = default)
        {
            IReadOnlyList<RetrievalResult> r =
                [.. candidates.OrderByDescending(c => c.Score).Take(topK)];
            return Task.FromResult(r);
        }
    }

    private sealed class StubCrossEncoderModel : ICrossEncoderModel
    {
        private readonly Func<string, float> _scoreOf;
        public StubCrossEncoderModel(Func<string, float> scoreOf) { _scoreOf = scoreOf; }
        public string ModelId => "stub-cross-encoder";
        public IReadOnlyList<float> Score(string query, IReadOnlyList<string> documents)
            => [.. documents.Select(_scoreOf)];
    }
}
