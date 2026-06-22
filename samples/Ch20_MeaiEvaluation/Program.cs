// Ch 20 — The .NET-native eval library: Microsoft.Extensions.AI.Evaluation.
//
// This is the first-party answer to "how do I score a RAG answer in .NET?" —
// the same library MAF's own agent evaluation builds on. It runs offline here.
//
// Two layers, cheapest first:
//
//   1) NLP BLEU (Microsoft.Extensions.AI.Evaluation.NLP) — a deterministic,
//      no-LLM n-gram overlap against a reference answer. Free and instant; use
//      it as a first-pass filter before spending judge tokens.
//
//   2) A CompositeEvaluator of three LLM-as-judge Quality evaluators —
//      RetrievalEvaluator (is the retrieved context relevant to the query?),
//      GroundednessEvaluator (is the answer anchored in that context?), and
//      RelevanceEvaluator (does the answer address the question?) — run together
//      over a ten-item SmartDocs HR set. The "LLM" judge here is a deterministic
//      stub wired in through ChatConfiguration, so the printed scores reproduce
//      run to run with no API key. Swap it for a real IChatClient to grade with
//      a frontier model.
//
// Each evaluator returns an EvaluationResult whose Metrics are NumericMetrics
// (Value in 1–5) carrying an Interpretation (Rating + Failed). We print each
// metric's value and rating per question, plus a mean.
//
// Run: dotnet run --project samples/Ch20_MeaiEvaluation

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.NLP;
using Microsoft.Extensions.AI.Evaluation.Quality;
using RagInDotNet.Samples.Ch20_MeaiEvaluation;

var cases = HrEvalSet.Build();

Console.WriteLine("=== Ch20: Microsoft.Extensions.AI.Evaluation over the SmartDocs HR set (offline) ===");
Console.WriteLine($"Cases: {cases.Count}");
Console.WriteLine();

// ── Layer 1: deterministic BLEU first-pass (no LLM, no key, no cost). ─────────
// BLEUEvaluator compares the answer against one or more reference answers via
// n-gram overlap. A low BLEU is a cheap signal to look closer before paying for
// a judge; it is NOT a quality verdict on its own (paraphrases score low).
var bleu = new BLEUEvaluator();
Console.WriteLine($"--- Layer 1: NLP {BLEUEvaluator.BLEUMetricName} (deterministic, no LLM) ---");
double bleuSum = 0;
foreach (var c in cases)
{
    var messages = new[] { new ChatMessage(ChatRole.User, c.Question) };
    var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, c.Answer));
    var bleuResult = await bleu.EvaluateAsync(
        messages, response,
        additionalContext: [new BLEUEvaluatorContext([c.ReferenceAnswer])]);

    var metric = bleuResult.Get<NumericMetric>(BLEUEvaluator.BLEUMetricName);
    var value = metric.Value ?? 0;
    bleuSum += value;
    Console.WriteLine($"  BLEU {value,5:F3}  {Truncate(c.Question, 52)}");
}
Console.WriteLine($"  mean BLEU = {bleuSum / cases.Count:F3}");
Console.WriteLine();

// ── Layer 2: the LLM-as-judge composite. ─────────────────────────────────────
// A deterministic stub judge stands in for a frontier model so the run is
// offline. It returns canned 1–5 verdicts keyed on the trait being graded; the
// one ungrounded answer (parental leave) is scored lower for groundedness.
var judge = new DeterministicJudge(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
{
    ["Retrieval"] = 4,
    ["Groundedness"] = 5,
    ["Relevance"] = 5,
});
var chatConfiguration = new ChatConfiguration(judge);

var composite = new CompositeEvaluator(
    new RetrievalEvaluator(),
    new GroundednessEvaluator(),
    new RelevanceEvaluator());

Console.WriteLine("--- Layer 2: CompositeEvaluator(Retrieval, Groundedness, Relevance) via stub judge ---");
Console.WriteLine($"{"question",-46} {"retrieval",10} {"grounded",10} {"relevance",10}");
Console.WriteLine(new string('-', 80));

var sums = new Dictionary<string, double>(StringComparer.Ordinal);
var counts = new Dictionary<string, int>(StringComparer.Ordinal);

foreach (var c in cases)
{
    var messages = new[] { new ChatMessage(ChatRole.User, c.Question) };
    var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, c.Answer));

    // Each evaluator needs its own context object:
    //   RetrievalEvaluator     ← the retrieved chunk texts
    //   GroundednessEvaluator  ← the grounding context (the same chunks, joined)
    //   RelevanceEvaluator     ← needs no extra context (query + response only)
    var additionalContext = new EvaluationContext[]
    {
        new RetrievalEvaluatorContext(c.RetrievedContext),
        new GroundednessEvaluatorContext(string.Join("\n", c.RetrievedContext)),
    };

    var result = await composite.EvaluateAsync(messages, response, chatConfiguration, additionalContext);

    var retrieval = Value(result, "Retrieval");
    var grounded = Value(result, "Groundedness");
    var relevance = Value(result, "Relevance");

    Accumulate(sums, counts, "Retrieval", retrieval);
    Accumulate(sums, counts, "Groundedness", grounded);
    Accumulate(sums, counts, "Relevance", relevance);

    Console.WriteLine(
        $"{Truncate(c.Question, 46),-46} " +
        $"{Format(result, "Retrieval"),10} " +
        $"{Format(result, "Groundedness"),10} " +
        $"{Format(result, "Relevance"),10}");
}

Console.WriteLine(new string('-', 80));
Console.WriteLine(
    $"{"mean",-46} " +
    $"{Mean(sums, counts, "Retrieval"),10:F2} " +
    $"{Mean(sums, counts, "Groundedness"),10:F2} " +
    $"{Mean(sums, counts, "Relevance"),10:F2}");
Console.WriteLine();
Console.WriteLine("Scores are 1–5 (higher is better). The stub judge makes them deterministic;");
Console.WriteLine("pass a real IChatClient to ChatConfiguration to grade with a frontier model.");

return 0;

static double Value(EvaluationResult result, string metricName)
{
    return result.Metrics.TryGetValue(metricName, out var metric) && metric is NumericMetric nm
        ? nm.Value ?? 0
        : 0;
}

static string Format(EvaluationResult result, string metricName)
{
    if (result.Metrics.TryGetValue(metricName, out var metric) && metric is NumericMetric nm)
    {
        var rating = nm.Interpretation?.Rating ?? EvaluationRating.Unknown;
        return $"{nm.Value ?? 0:F0} ({Abbrev(rating)})";
    }
    return "n/a";
}

static string Abbrev(EvaluationRating rating) => rating switch
{
    EvaluationRating.Exceptional => "exc",
    EvaluationRating.Good => "good",
    EvaluationRating.Average => "avg",
    EvaluationRating.Poor => "poor",
    EvaluationRating.Unacceptable => "bad",
    EvaluationRating.Inconclusive => "inc",
    _ => "?",
};

static void Accumulate(Dictionary<string, double> sums, Dictionary<string, int> counts, string key, double value)
{
    sums[key] = sums.GetValueOrDefault(key) + value;
    counts[key] = counts.GetValueOrDefault(key) + 1;
}

static double Mean(Dictionary<string, double> sums, Dictionary<string, int> counts, string key) =>
    counts.GetValueOrDefault(key) == 0 ? 0 : sums[key] / counts[key];

static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
