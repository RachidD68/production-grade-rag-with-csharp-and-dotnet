using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.NLP;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace SmartDocs.UnitTests.Evaluation;

/// <summary>
/// Verifies the Microsoft.Extensions.AI.Evaluation wiring used by the
/// Ch20_MeaiEvaluation sample runs offline: a CompositeEvaluator of the three
/// RAG Quality evaluators, judged by a deterministic stub IChatClient through
/// ChatConfiguration, plus the deterministic NLP BLEU evaluator. These tests
/// pin the exact verified API surface so the manuscript can match it.
/// </summary>
public sealed class MeaiEvaluationTests
{
    // The Quality evaluators send a rubric prompt and expect the verdict wrapped
    // in tagged sections; the integer 1–5 score goes in <S2>…</S2>. A bare number
    // does NOT parse. This stub returns a fixed tagged score so the run is offline
    // and deterministic.
    private sealed class TaggedScoreJudge(int score) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var verdict = $"<S0>Let's think step by step: ok.</S0><S1>Deterministic.</S1><S2>{score}</S2>";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, verdict)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task CompositeEvaluator_scores_retrieval_groundedness_and_relevance_offline()
    {
        var composite = new CompositeEvaluator(
            new RetrievalEvaluator(),
            new GroundednessEvaluator(),
            new RelevanceEvaluator());

        var chatConfiguration = new ChatConfiguration(new TaggedScoreJudge(score: 4));

        var context = "Employees accrue twenty paid vacation days per fiscal year.";
        var messages = new[] { new ChatMessage(ChatRole.User, "How many vacation days do I get?") };
        var response = new ChatResponse(new ChatMessage(
            ChatRole.Assistant, "Employees get twenty paid vacation days per fiscal year."));

        var additionalContext = new EvaluationContext[]
        {
            new RetrievalEvaluatorContext([context]),
            new GroundednessEvaluatorContext(context),
        };

        var result = await composite.EvaluateAsync(messages, response, chatConfiguration, additionalContext);

        // All three metrics present, parsed to the score the stub judge returned.
        foreach (var metricName in new[] { "Retrieval", "Groundedness", "Relevance" })
        {
            Assert.True(result.Metrics.ContainsKey(metricName), $"missing metric {metricName}");
            var metric = result.Get<NumericMetric>(metricName);
            Assert.Equal(4.0, metric.Value);
            Assert.Equal(EvaluationRating.Good, metric.Interpretation?.Rating);
        }
    }

    [Fact]
    public async Task Bleu_evaluator_scores_an_exact_match_as_one_offline()
    {
        var bleu = new BLEUEvaluator();
        const string answer = "Employees get twenty paid vacation days per fiscal year.";

        var messages = new[] { new ChatMessage(ChatRole.User, "How many vacation days?") };
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));

        var result = await bleu.EvaluateAsync(
            messages, response,
            additionalContext: [new BLEUEvaluatorContext([answer])]);

        var metric = result.Get<NumericMetric>(BLEUEvaluator.BLEUMetricName);
        // An identical reference scores a perfect BLEU of 1.0 — and it's deterministic.
        Assert.Equal(1.0, metric.Value!.Value, precision: 6);
    }
}
