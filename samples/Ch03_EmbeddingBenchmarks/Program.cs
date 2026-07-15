// Chapter 3 — Embeddings — Turning Text into Vectors.
//
// Benchmarks one or more local Ollama embedding models on a fixed set of
// 50 passages drawn from the Contoso HR silo. For each model:
//   - per-embedding latency (mean, p50, p95)
//   - total batch wall time
//   - dimensionality
//   - cosine-similarity quality on 10 hand-picked similar/dissimilar pairs
//
// To add Azure OpenAI / Cohere / Voyage models, register their adapters
// and append to the `models` list. The wiring works as long as the model
// implements IEmbeddingGenerator<string, Embedding<float>> from
// Microsoft.Extensions.AI.

using System.Diagnostics;
using Microsoft.Extensions.AI;
using OllamaSharp;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

var endpoint = new Uri(Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434");

// --no-prefix runs every model a second time with EmbeddingPrompt.None so the
// reader sees the quality margin move when the task prefixes are removed. The
// default run already prints the correctly-prefixed row.
var compareNoPrefix = args.Contains("--no-prefix", StringComparer.OrdinalIgnoreCase);

// Each model is paired with the task-prefix scheme it was trained to expect:
//   nomic-embed-text   -> search_document: / search_query:
//   mxbai-embed-large  -> query-only "Represent this sentence ..."
//   OpenAI / Azure      -> EmbeddingPrompt.None (trained without prefixes).
// Add additional models here once they're pulled with `ollama pull <name>`.
var models = new (string Name, IEmbeddingGenerator<string, Embedding<float>> Gen, EmbeddingPrompt Prompt)[]
{
    ("nomic-embed-text", new OllamaApiClient(endpoint, "nomic-embed-text"), EmbeddingPrompt.Nomic),
    // ("mxbai-embed-large", new OllamaApiClient(endpoint, "mxbai-embed-large"), EmbeddingPrompt.Mxbai),
};

var passages = LoadPassages();
var (similarPairs, dissimilarPairs) = LoadPairs();

Console.WriteLine($"Embedding benchmark — {passages.Length} passages × {models.Length} models");
Console.WriteLine();
Console.WriteLine($"| Model               | Prefix | Dim  | Mean ms | p50 ms | p95 ms | Sim avg | Dis avg | Margin |");
Console.WriteLine($"|---------------------|--------|-----:|--------:|-------:|-------:|--------:|--------:|-------:|");

foreach (var (name, gen, prompt) in models)
{
    var lats = new List<long>();
    GeneratedEmbeddings<Embedding<float>>? lastBatch = null;
    foreach (var p in passages)
    {
        var single = Stopwatch.StartNew();
        // Latency timing embeds passages as documents — the corpus-side cost.
        lastBatch = await gen.GenerateAsync(new[] { prompt.Apply(p, EmbeddingTaskType.Document) });
        single.Stop();
        lats.Add(single.ElapsedMilliseconds);
    }

    var dim = lastBatch?[0].Vector.Length ?? 0;
    lats.Sort();
    var mean = lats.Average();
    var p50 = lats[lats.Count / 2];
    var p95 = lats[(int)(lats.Count * 0.95)];

    // The correctly-prefixed (asymmetric) row.
    await PrintQualityRowAsync(name, "yes", dim, mean, p50, p95, gen, prompt);

    // The without-prefix row — same model, EmbeddingPrompt.None — so the margin
    // delta is visible side by side. This is the chapter's headline demonstration.
    if (compareNoPrefix)
    {
        await PrintQualityRowAsync(name, "no", dim, mean, p50, p95, gen, EmbeddingPrompt.None);
    }
}

Console.WriteLine();
Console.WriteLine("Wider quality margin (Sim - Dis) = better semantic separation.");
if (compareNoPrefix)
{
    Console.WriteLine("Compare the 'yes' and 'no' rows: removing the task prefixes shrinks the margin.");
}

async Task PrintQualityRowAsync(
    string name, string prefixFlag, int dim, double mean, long p50, long p95,
    IEmbeddingGenerator<string, Embedding<float>> gen, EmbeddingPrompt prompt)
{
    var simCos = await AvgCosineAsync(gen, prompt, similarPairs);
    var disCos = await AvgCosineAsync(gen, prompt, dissimilarPairs);
    Console.WriteLine($"| {name,-19} | {prefixFlag,-6} | {dim,4} | {mean,7:F0} | {p50,6} | {p95,6} | {simCos,7:F3} | {disCos,7:F3} | {(simCos - disCos),6:F3} |");
}

static async Task<double> AvgCosineAsync(
    IEmbeddingGenerator<string, Embedding<float>> gen,
    EmbeddingPrompt prompt,
    (string Query, string Passage)[] pairs)
{
    var sum = 0.0;
    foreach (var (q, p) in pairs)
    {
        var emb = await gen.GenerateAsync(new[]
        {
            prompt.Apply(q, EmbeddingTaskType.Query),
            prompt.Apply(p, EmbeddingTaskType.Document),
        });
        sum += Cosine(emb[0].Vector.Span, emb[1].Vector.Span);
    }
    return sum / pairs.Length;
}

static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
{
    double dot = 0, ma = 0, mb = 0;
    for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; ma += a[i] * a[i]; mb += b[i] * b[i]; }
    return ma == 0 || mb == 0 ? 0 : dot / (Math.Sqrt(ma) * Math.Sqrt(mb));
}

static string[] LoadPassages() =>
[
    "Employees at the Montreal office receive 20 paid vacation days per fiscal year.",
    "Sick leave is unlimited for employees in good standing.",
    "Remote work is allowed up to 3 days per week with manager approval.",
    "Annual performance reviews occur in March; salary adjustments take effect on May 1.",
    "Parental leave provides 18 weeks of fully paid time off.",
    "The travel reimbursement form must be filed within 30 days of return.",
    "Health benefits include dental, vision, and a $500 wellness stipend.",
    "Pension contributions are matched up to 5% of base salary.",
    "Stock options vest over four years with a one-year cliff.",
    "Probation lasts 90 days for new permanent hires.",
    // Repeat with paraphrases / topic shifts to get to 50 — truncated here for brevity in source view.
    "Vacation days roll over up to a maximum of 10 unused days into the next fiscal year.",
    "Long-term sick leave beyond 10 days requires a medical certificate.",
    "The remote-work policy was last updated in fiscal year 2025.",
    "Performance ratings drive both bonus and equity refresh cycles.",
    "Parental leave can be taken in non-consecutive blocks with manager approval.",
    "Per diem for international travel is set at $75 USD per day.",
    "Vision care is covered up to $300 per two-year period.",
    "401(k) and equivalent retirement plans differ by office geography.",
    "Equity grants are documented in a separate Stock Option Agreement.",
    "Probation can be extended by 30 days at the manager's discretion.",
    "The Paris office observes French statutory holidays in addition to company holidays.",
    "Casablanca employees are entitled to two days of religious observance leave per year.",
    "Code of conduct violations are handled by HR Business Partners with escalation paths.",
    "Anti-harassment training is mandatory annually for all staff.",
    "Office hours are 09:00 to 18:00 with a flexible two-hour core.",
    "Onboarding includes a one-week orientation across product and engineering tracks.",
    "Offboarding requires laptop return within five business days of the last working day.",
    "Internal mobility postings are visible to employees with at least 12 months of tenure.",
    "Whistleblower reports can be filed anonymously through the secure portal.",
    "Salary bands are reviewed annually against external benchmark surveys.",
    "Bereavement leave provides up to five paid days for immediate family.",
    "The expense policy distinguishes per diem from itemized reimbursement.",
    "Employees opting for hybrid work must meet on-site at least twice per week.",
    "Equity refreshes follow the annual review cycle and depend on performance rating.",
    "Departmental travel budgets reset at the start of each fiscal year.",
    "Health benefit changes during open enrolment apply for the next twelve months.",
    "Pension vesting schedules are documented in the employee handbook appendix.",
    "Stock option exercises are subject to insider-trading windows.",
    "New hires receive a $1,500 home-office stipend in the first 60 days.",
    "Internal mobility offers a relocation allowance of up to $10,000.",
    "All-hands meetings are held the first Friday of every month.",
    "The diversity & inclusion council meets quarterly with executive sponsors.",
    "Office closures during national holidays follow the local public-holiday calendar.",
    "Remote workers are eligible for monthly internet stipends up to $80.",
    "Sabbatical leave of up to three months is available after seven years of service.",
    "Education reimbursement covers up to $5,000 per fiscal year.",
    "Paid jury-duty leave is unlimited and does not count against vacation days.",
    "Employee referrals carry a $2,000 bonus on successful placement.",
    "Performance Improvement Plans last 60 to 90 days with weekly check-ins.",
    "Exit interviews are scheduled within the final week of employment.",
];

static ((string, string)[] Similar, (string, string)[] Dissimilar) LoadPairs() =>
(
    Similar:
    [
        ("How many vacation days?", "Employees receive 20 paid vacation days per fiscal year."),
        ("What's the parental leave policy?", "Parental leave provides 18 weeks of fully paid time off."),
        ("How does sick leave work?", "Sick leave is unlimited for employees in good standing."),
        ("Tell me about remote work", "Remote work is allowed up to 3 days per week."),
        ("What's the probation duration?", "Probation lasts 90 days for new permanent hires."),
        ("Are there stock options?", "Stock options vest over four years with a one-year cliff."),
        ("Health benefits coverage?", "Health benefits include dental, vision, and a wellness stipend."),
        ("Pension contributions?", "Pension contributions are matched up to 5% of base salary."),
        ("Performance review cycle?", "Annual performance reviews occur in March."),
        ("Travel reimbursement deadline?", "Travel reimbursement must be filed within 30 days of return."),
    ],
    Dissimilar:
    [
        ("How many vacation days?", "Stock option exercises are subject to insider-trading windows."),
        ("Parental leave policy?", "The diversity & inclusion council meets quarterly."),
        ("How does sick leave work?", "Office hours are 09:00 to 18:00 with a flexible core."),
        ("Tell me about remote work", "Bereavement leave provides up to five paid days."),
        ("Probation duration?", "All-hands meetings are held the first Friday of every month."),
        ("Stock options?", "Onboarding includes a one-week orientation."),
        ("Health benefits coverage?", "Whistleblower reports can be filed anonymously."),
        ("Pension contributions?", "Anti-harassment training is mandatory annually."),
        ("Performance reviews?", "Casablanca employees get two religious observance days."),
        ("Travel reimbursement?", "Sabbatical leave is available after seven years."),
    ]
);

namespace RagInDotNet.Samples.Ch03_EmbeddingBenchmarks
{
    /// <summary>Marker for the Ch03 sample assembly.</summary>
    internal static class Marker { public const string Project = nameof(Ch03_EmbeddingBenchmarks); }
}
