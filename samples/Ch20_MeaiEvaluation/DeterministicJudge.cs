using Microsoft.Extensions.AI;

namespace RagInDotNet.Samples.Ch20_MeaiEvaluation;

/// <summary>
/// A fully deterministic stand-in for the LLM judge the
/// Microsoft.Extensions.AI.Evaluation Quality evaluators call. A real run would
/// pass a frontier model here through <c>ChatConfiguration</c>; this stub lets
/// the sample run offline in CI with no key while still exercising the genuine
/// evaluator code path (prompt construction, response parsing, metric
/// interpretation).
///
/// <para>
/// The Quality evaluators send a structured rubric prompt and expect the verdict
/// wrapped in tagged sections — chain-of-thought in <c>&lt;S0&gt;</c>, a short
/// explanation in <c>&lt;S1&gt;</c>, and an integer 1–5 score in <c>&lt;S2&gt;</c>.
/// Returning a bare number does NOT parse. This stub reads the trait being
/// graded from the rubric text and returns a canned, plausible score in the
/// exact tagged shape the parser accepts, so the printed metrics are stable run
/// to run.
/// </para>
/// </summary>
internal sealed class DeterministicJudge(IReadOnlyDictionary<string, int> scoresByTrait, int defaultScore = 4)
    : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = string.Join("\n", messages.Select(m => m.Text));
        var score = ScoreFor(prompt);
        var verdict =
            "<S0>Let's think step by step: the response is judged against the rubric " +
            "and the supplied data.</S0>" +
            "<S1>Deterministic offline verdict for a reproducible CI run.</S1>" +
            $"<S2>{score}</S2>";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, verdict)));
    }

    // The rubric prompt names the trait (e.g. "**Groundedness**", "**Relevance**",
    // "**Retrieval**") in its Definition section. Match on that to return the
    // configured score; fall back to the default otherwise.
    //
    // One deterministic exception keeps the demo honest: a groundedness grade for
    // an answer that contradicts its context (the seeded "twenty-four weeks"
    // parental-leave claim, whose context says "sixteen weeks") is dropped to a
    // failing 2, so the printed table shows groundedness actually discriminating
    // a hallucination — not a flat column of fives.
    private int ScoreFor(string prompt)
    {
        foreach (var (trait, score) in scoresByTrait)
        {
            if (prompt.Contains(trait, StringComparison.OrdinalIgnoreCase))
            {
                var isGroundedness = trait.Equals("Groundedness", StringComparison.OrdinalIgnoreCase);
                if (isGroundedness &&
                    prompt.Contains("twenty-four weeks", StringComparison.OrdinalIgnoreCase) &&
                    prompt.Contains("sixteen weeks", StringComparison.OrdinalIgnoreCase))
                {
                    return 2; // answer not anchored in the provided context
                }
                return score;
            }
        }
        return defaultScore;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        // Nothing to dispose; the judge is pure computation.
    }
}
