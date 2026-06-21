// Run: dotnet run --project samples/Ch09_RerankingEval
//
// Chapter 9 — Reranking.
//
// A reranking comparison harness. It builds a fixed SmartDocs/Contoso corpus,
// indexes it into an in-memory vector store with a deterministic bag-of-words
// embedder, and stands up one base dense retriever. It then evaluates three
// arms over the same gold set, each wrapping the base retriever in the
// production RerankingMiddleware:
//
//   * baseline  — NoOpReranker (pass-through; the control arm)
//   * llm       — LlmRerank driven by a deterministic lexical-overlap stub chat
//                 client (no model, no key, no network)
//   * cohere    — CohereReranker, ONLY when COHERE_API_KEY is set; otherwise the
//                 arm prints "skipped (no COHERE_API_KEY)"
//
// Fully offline and deterministic by default: the printed table reproduces run
// to run. Teaching point — reranking lifts the rank-sensitive metrics (nDCG@5
// and MRR), which reward putting the right document first, more than recall@5,
// which only asks whether the right document is somewhere in the top-K.

using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using RagInDotNet.Samples.Ch09_RerankingEval;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Evaluation;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Reranking;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.VectorStores;

Console.WriteLine("=== Ch09: Reranking — baseline / llm / cohere over recall@5, nDCG@5, MRR ===");
Console.WriteLine();

var chunks = Corpus.BuildChunks();
var gold = Corpus.BuildGoldQueries();
const int K = Corpus.GoldK;

// --- Base dense retriever: deterministic bag-of-words embedder, in-memory store.
var embedder = new BagOfWordsEmbeddingGenerator();
var embeddingService = new EmbeddingService(
    embedder, "bag-of-words-256", 256, NullLogger<EmbeddingService>.Instance);

var store = new InMemoryVectorStore("ch09-eval");
await store.EnsureCollectionExistsAsync().ConfigureAwait(false);
foreach (var chunk in chunks)
{
    var embedded = await embeddingService.EmbedAsync(chunk).ConfigureAwait(false);
    await store.UpsertAsync([embedded]).ConfigureAwait(false);
}
var baseRetriever = new DenseRetriever(embeddingService, store);

Console.WriteLine(
    $"Corpus: {chunks.Count} chunks across {chunks.Select(c => c.DocumentId).Distinct().Count()} documents | " +
    $"Gold queries: {gold.Count}, metrics @ {K}");
Console.WriteLine("Embedder: deterministic bag-of-words (offline). Reranking judge: lexical-overlap stub.");
Console.WriteLine();

// --- Build the arms. Each wraps the SAME base retriever in RerankingMiddleware.
var arms = new List<(string Label, IRetriever? Retriever)>
{
    ("baseline (no-op)", new RerankingMiddleware(baseRetriever, new NoOpReranker(), candidateCount: 20)),
    ("llm (stub judge)", new RerankingMiddleware(baseRetriever, new LlmRerank(new LexicalOverlapChatClient()), candidateCount: 20)),
};

// Cohere arm: real API, so only when a key is present.
HttpClient? cohereHttp = null;
var cohereKey = Environment.GetEnvironmentVariable("COHERE_API_KEY");
if (!string.IsNullOrWhiteSpace(cohereKey))
{
    cohereHttp = new HttpClient();
    arms.Add(("cohere (rerank-v3.5)",
        new RerankingMiddleware(baseRetriever, new CohereReranker(cohereHttp, cohereKey), candidateCount: 20)));
}
else
{
    arms.Add(("cohere (rerank-v3.5)", null)); // rendered as "skipped" below
}

// --- Evaluate each arm over the gold set. -------------------------------------
var rows = new List<(string Label, RetrievalMetrics? Metrics)>(arms.Count);
foreach (var (label, retriever) in arms)
{
    if (retriever is null)
    {
        rows.Add((label, null));
        continue;
    }

    var samples = new List<(GoldenItem Gold, IReadOnlyList<RetrievalResult> Hits)>(gold.Count);
    foreach (var item in gold)
    {
        var hits = await retriever.RetrieveAsync(item.Query, K).ConfigureAwait(false);
        samples.Add((item, hits));
    }
    rows.Add((label, RetrievalEvaluator.Evaluate(samples, K)));
}

cohereHttp?.Dispose();

// --- Comparison table. --------------------------------------------------------
Console.WriteLine($"{"arm",-24} {$"recall@{K}",10} {$"nDCG@{K}",10} {"MRR",8}");
Console.WriteLine(new string('-', 56));
foreach (var (label, metrics) in rows)
{
    if (metrics is null)
    {
        Console.WriteLine($"{label,-24} {"skipped (no COHERE_API_KEY)",30}");
        continue;
    }

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{label,-24} {metrics.RecallAtK,10:P1} {metrics.NdcgAtK,10:P1} {metrics.Mrr,8:F3}"));
}

Console.WriteLine();
Console.WriteLine($"Metrics measured at k={K} over {gold.Count} gold queries, matched on DocumentId.");
Console.WriteLine("Reranking lifts the rank-sensitive metrics (nDCG@5, MRR) more than recall@5:");
Console.WriteLine("recall only asks whether the right doc is in the top-K; nDCG and MRR reward ranking it first.");
return 0;
