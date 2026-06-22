// tools/eval-runner — the offline RAG eval gate (Option B).
//
// Runs the retrieval + faithfulness eval over a fixed, deterministic SmartDocs
// HR corpus and prints each metric WITH a confidence interval, so a reader can
// see whether a number is solid or inside the noise band of a small seed set:
//
//   • Retrieval proportions (recall@K, hit-rate) get a Wilson score interval.
//   • The mean faithfulness score gets a seeded bootstrap interval.
//
// Given a baseline JSON (--baseline path), it also reports the per-item paired
// delta in faithfulness between the baseline run and this run, and whether that
// delta's CI excludes zero — the real promotion-gate question ("block if the
// delta's CI excludes zero in the wrong direction"), not a magic "down 2 points".
//
// Everything is offline and deterministic: a bag-of-words embedder and a
// deterministic faithfulness judge stand in for a model, so the numbers — and
// their CIs — reproduce in CI with no key.
//
// Run (report only):       dotnet run --project tools/eval-runner
// Write a baseline:        dotnet run --project tools/eval-runner -- --write-baseline eval-baseline.json
// Compare against baseline: dotnet run --project tools/eval-runner -- --baseline eval-baseline.json
//
// NOTE: the CI workflow that fails the build on a significant regression belongs
// in Chapter 25 (.github/workflows/eval-gate.yml) — this runner is the engine it
// would call; it is intentionally not wired to a workflow here.

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RagInDotNet.Tools.EvalRunner;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Evaluation;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.VectorStores;

const int BootstrapSeed = 20_260_622; // fixed seed → reproducible confidence intervals.

string? baselinePath = GetOption(args, "--baseline");
string? writeBaselinePath = GetOption(args, "--write-baseline");

// --gate enforces the promotion gate in CI (Ch 25 deploy-prod.yml). With no
// explicit --baseline it compares against the committed default baseline, and
// blocks on a statistically-significant faithfulness regression OR an absolute
// recall / faithfulness drop beyond the supplied thresholds.
const string DefaultBaselinePath = "eval-baseline.json";
bool gate = args.Contains("--gate");
double recallThreshold = GetDoubleOption(args, "--recall-threshold") ?? 0.02;
double faithfulnessThreshold = GetDoubleOption(args, "--faithfulness-threshold") ?? 0.03;
string? effectiveBaseline = baselinePath ?? (gate ? DefaultBaselinePath : null);

Console.WriteLine("=== eval-runner: offline retrieval + faithfulness eval with confidence intervals ===");
Console.WriteLine();

// --- Build the offline retrieval stack (bag-of-words dense retriever). --------
var chunks = EvalCorpus.BuildChunks();
var cases = EvalCorpus.BuildCases();
const int k = EvalCorpus.K;

var embeddingService = new EmbeddingService(
    new BagOfWordsEmbeddingGenerator(),
    embeddingModel: "bag-of-words-256",
    dimensions: BagOfWordsEmbeddingGenerator.Dimensions,
    NullLogger<EmbeddingService>.Instance,
    EmbeddingPrompt.None);

var store = new InMemoryVectorStore("eval-runner");
await store.EnsureCollectionExistsAsync();
foreach (var chunk in chunks)
{
    var embedded = await embeddingService.EmbedAsync(chunk);
    await store.UpsertAsync([embedded]);
}
var retriever = new DenseRetriever(embeddingService, store);

// --- Retrieve for every gold query and score retrieval + faithfulness. --------
var faithfulnessJudge = new DeterministicFaithfulnessJudge();
var generationEvaluator = new GenerationEvaluator(faithfulnessJudge);

var retrievalSamples = new List<(GoldenItem Gold, IReadOnlyList<RetrievalResult> Hits)>(cases.Count);
var perQueryHit = new List<bool>(cases.Count);            // did top-K contain a gold doc?
var perQueryFaithfulness = new List<double>(cases.Count); // faithfulness of the reference answer.

foreach (var c in cases)
{
    var hits = await retriever.RetrieveAsync(c.Gold.Query, k);
    retrievalSamples.Add((c.Gold, hits));

    var retrievedIds = hits.Take(k).Select(h => h.Chunk.DocumentId).ToHashSet(StringComparer.Ordinal);
    perQueryHit.Add(retrievedIds.Overlaps(c.Gold.ExpectedDocumentIds));

    // Faithfulness of the reference answer against the retrieved context.
    var assessment = await generationEvaluator.AssessAsync(c.Gold.Query, c.ReferenceAnswer, hits);
    perQueryFaithfulness.Add(assessment.FaithfulnessScore);
}

var retrieval = RetrievalEvaluator.Evaluate(retrievalSamples, k);

// --- Confidence intervals. ----------------------------------------------------
int hitCount = perQueryHit.Count(h => h);
var hitRateCi = StatisticalSignificance.WilsonInterval(hitCount, cases.Count);
var faithfulnessCi = StatisticalSignificance.BootstrapMean(perQueryFaithfulness, BootstrapSeed);
double meanFaithfulness = perQueryFaithfulness.Average();

Console.WriteLine($"Corpus: {chunks.Count} chunks | Gold queries: {cases.Count} | K={k}");
Console.WriteLine();
Console.WriteLine($"{"metric",-22} {"value",8}   {"95% CI",-22} note");
Console.WriteLine(new string('-', 74));
Console.WriteLine($"{"recall@" + k,-22} {retrieval.RecallAtK,8:P1}   {"",-22} (point estimate)");
Console.WriteLine($"{"precision@" + k,-22} {retrieval.PrecisionAtK,8:P1}   {"",-22} (point estimate)");
Console.WriteLine($"{"MRR",-22} {retrieval.Mrr,8:F3}   {"",-22} (point estimate)");
Console.WriteLine($"{"nDCG@" + k,-22} {retrieval.NdcgAtK,8:P1}   {"",-22} (point estimate)");
Console.WriteLine($"{"hit-rate@" + k,-22} {(double)hitCount / cases.Count,8:P1}   {FormatCi(hitRateCi, percent: true),-22} Wilson, n={cases.Count}");
Console.WriteLine($"{"faithfulness (mean)",-22} {meanFaithfulness,8:F3}   {FormatCi(faithfulnessCi, percent: false),-22} bootstrap, seed={BootstrapSeed}");
Console.WriteLine();

// --- Baseline comparison (paired). --------------------------------------------
int exitCode = 0;
if (effectiveBaseline is not null)
{
    if (!File.Exists(effectiveBaseline))
    {
        if (gate)
        {
            // First gated run with no committed baseline: bootstrap one and pass,
            // so the gate becomes enforceable from the next run onward.
            var seed = new EvalBaseline(perQueryFaithfulness, perQueryHit);
            await File.WriteAllTextAsync(
                effectiveBaseline, JsonSerializer.Serialize(seed, EvalJsonContext.Default.EvalBaseline));
            Console.WriteLine($"GATE: PASS — no baseline at {effectiveBaseline}; bootstrapped one from this run.");
            return 0;
        }
        Console.Error.WriteLine($"Baseline file not found: {effectiveBaseline}");
        return 2;
    }
    var baseline = JsonSerializer.Deserialize(
        await File.ReadAllTextAsync(effectiveBaseline), EvalJsonContext.Default.EvalBaseline);
    if (baseline is null || baseline.PerQueryFaithfulness.Count != perQueryFaithfulness.Count)
    {
        Console.Error.WriteLine("Baseline is malformed or its seed set size does not match the current run.");
        return 2;
    }

    var verdict = StatisticalSignificance.PairedBootstrapDelta(
        baseline.PerQueryFaithfulness, perQueryFaithfulness, BootstrapSeed);
    var mcnemar = StatisticalSignificance.McNemar(
        baseline.PerQueryHit, perQueryHit);

    Console.WriteLine("--- Comparison against baseline (paired) ---");
    Console.WriteLine($"Faithfulness delta (candidate - baseline): {verdict.Delta:+0.000;-0.000;0.000}");
    Console.WriteLine($"  95% CI: {FormatCi(verdict.Interval, percent: false)}  " +
                      $"({(verdict.IsSignificant ? "excludes zero — significant" : "straddles zero — within noise")})");
    Console.WriteLine($"Retrieval hit flips (McNemar): " +
                      $"regressions={mcnemar.BaselinePassCandidateFail}, fixes={mcnemar.BaselineFailCandidatePass}, " +
                      $"p={mcnemar.PValue:F3} ({(mcnemar.IsSignificant() ? "significant" : "not significant")})");
    Console.WriteLine();

    if (verdict.IsSignificantRegression)
    {
        Console.WriteLine("GATE: BLOCK — faithfulness regressed and the delta's CI excludes zero in the wrong direction.");
        exitCode = 1;
    }
    else
    {
        Console.WriteLine("GATE: PASS — no significant regression (delta CI does not exclude zero in the wrong direction).");
    }

    // Absolute-threshold gates (the --recall-threshold / --faithfulness-threshold
    // the deploy workflow passes): block if either metric drops beyond tolerance.
    if (gate)
    {
        double baselineHitRate = baseline.PerQueryHit.Count(h => h) / (double)baseline.PerQueryHit.Count;
        double currentHitRate = (double)hitCount / cases.Count;
        double baselineMeanFaithfulness = baseline.PerQueryFaithfulness.Average();
        double recallDrop = baselineHitRate - currentHitRate;
        double faithfulnessDrop = baselineMeanFaithfulness - meanFaithfulness;
        Console.WriteLine(
            $"GATE thresholds: hit-rate drop {recallDrop:+0.000;-0.000;0.000} (max {recallThreshold:F3}), " +
            $"faithfulness drop {faithfulnessDrop:+0.000;-0.000;0.000} (max {faithfulnessThreshold:F3})");
        if (recallDrop > recallThreshold)
        {
            Console.WriteLine("GATE: BLOCK — hit-rate regressed beyond --recall-threshold.");
            exitCode = 1;
        }
        if (faithfulnessDrop > faithfulnessThreshold)
        {
            Console.WriteLine("GATE: BLOCK — faithfulness regressed beyond --faithfulness-threshold.");
            exitCode = 1;
        }
    }
}

// --- Optionally persist this run as the new baseline. -------------------------
if (writeBaselinePath is not null)
{
    var snapshot = new EvalBaseline(perQueryFaithfulness, perQueryHit);
    var json = JsonSerializer.Serialize(snapshot, EvalJsonContext.Default.EvalBaseline);
    await File.WriteAllTextAsync(writeBaselinePath, json);
    Console.WriteLine($"Wrote baseline to {writeBaselinePath}");
}

return exitCode;

static string? GetOption(string[] args, string name)
{
    var idx = Array.IndexOf(args, name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

static double? GetDoubleOption(string[] args, string name)
{
    var raw = GetOption(args, name);
    return raw is not null
        && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
        ? v
        : null;
}

static string FormatCi(ConfidenceInterval ci, bool percent) =>
    percent
        ? $"[{ci.Lower:P1}, {ci.Upper:P1}]"
        : $"[{ci.Lower:F3}, {ci.Upper:F3}]";
