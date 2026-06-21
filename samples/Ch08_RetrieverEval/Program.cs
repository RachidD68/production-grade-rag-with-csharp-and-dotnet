// Chapter 8 — The Retriever.
//
// A recall@10 comparison harness for the three retrievers the chapter teaches:
// dense (vector search), sparse (in-process BM25), and hybrid (RRF fusion of the
// two). It builds a fixed 30-document SmartDocs corpus, indexes it into an
// in-memory vector store and a BM25 index, then runs the same 50-query gold set
// through each retriever and reports recall@10 (plus precision@10, MRR, and
// nDCG@10) using the production RetrievalEvaluator — no new metric is invented.
//
// Deterministic and offline by default: a bag-of-words embedder with a stable
// FNV-1a hash stands in for a real model, so the printed numbers reproduce in CI
// with no key. Set CH08_USE_OLLAMA=1 (optionally OLLAMA_ENDPOINT) to swap the
// embedder for a live Ollama model (nomic-embed-text); the sparse and hybrid
// legs are unaffected.
//
// Run (offline, default):
//   dotnet run --project samples/Ch08_RetrieverEval
//
// Run with a real embedder:
//   CH08_USE_OLLAMA=1 dotnet run --project samples/Ch08_RetrieverEval

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OllamaSharp;
using RagInDotNet.Samples.Ch08_RetrieverEval;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Evaluation;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.VectorStores;

Console.WriteLine("=== Ch08: The Retriever — dense / sparse / hybrid recall@10 ===");
Console.WriteLine();

var chunks = Corpus.BuildChunks();
var gold = Corpus.BuildGoldQueries();
const int K = Corpus.GoldK;

// --- Embedder: deterministic bag-of-words by default; Ollama on request. -----
IEmbeddingGenerator<string, Embedding<float>> embedder = new BagOfWordsEmbeddingGenerator();
var embeddingModelName = "bag-of-words-256";
var embeddingDimensions = 256;
var prompt = EmbeddingPrompt.None;

if (Environment.GetEnvironmentVariable("CH08_USE_OLLAMA") == "1")
{
    var endpoint = new Uri(
        Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434");
    const string OllamaEmbeddingModel = "nomic-embed-text";
    if (await IsOllamaReachableAsync(endpoint).ConfigureAwait(false))
    {
        embedder = new OllamaApiClient(endpoint, OllamaEmbeddingModel);
        embeddingModelName = OllamaEmbeddingModel;
        embeddingDimensions = 768;
        // nomic-embed-text is instruction-tuned: it expects asymmetric
        // search_document: / search_query: prefixes, applied by EmbeddingService.
        prompt = EmbeddingPrompt.Nomic;
        Console.WriteLine($"Embedder: Ollama '{OllamaEmbeddingModel}' at {endpoint}.");
    }
    else
    {
        Console.WriteLine($"CH08_USE_OLLAMA=1 but Ollama is not reachable at {endpoint}.");
        Console.WriteLine("  Falling back to the deterministic bag-of-words embedder.");
    }
}
else
{
    Console.WriteLine("Embedder: deterministic bag-of-words (offline). Set CH08_USE_OLLAMA=1 for Ollama.");
}

Console.WriteLine(
    $"Corpus: {chunks.Count} chunks across {chunks.Select(c => c.DocumentId).Distinct().Count()} documents | " +
    $"Gold queries: {gold.Count}, recall@{K}");
Console.WriteLine();

// --- Build the embedding service (the domain port DenseRetriever depends on). -
// EmbeddingService routes queries through EmbedQueryAsync with the model's query
// prefix scheme; EmbeddingPrompt.None is a pass-through for the offline embedder.
var embeddingService = new EmbeddingService(
    embedder, embeddingModelName, embeddingDimensions, NullLogger<EmbeddingService>.Instance, prompt);

// --- Dense leg: embed the corpus and upsert into the in-memory vector store. --
var store = new InMemoryVectorStore("ch08-eval");
await store.EnsureCollectionExistsAsync().ConfigureAwait(false);
foreach (var chunk in chunks)
{
    var embedded = await embeddingService.EmbedAsync(chunk).ConfigureAwait(false);
    await store.UpsertAsync([embedded]).ConfigureAwait(false);
}
var dense = new DenseRetriever(embeddingService, store);

// --- Sparse leg: build the in-process BM25 index over the same chunks. --------
var sparse = new SparseRetriever();
sparse.Index(chunks);

// --- Hybrid leg: the mode factory the chapter teaches (dense/sparse/hybrid). --
// Picking the mode by string is exactly what RetrieverModeFactory does; here we
// ask it for the hybrid composition so the sample exercises the factory too.
var hybrid = RetrieverModeFactory.Create("hybrid", dense, sparse);

var retrievers = new[] { dense, sparse, hybrid };

// --- Evaluate each retriever over the gold set. -------------------------------
var rows = new List<(string Strategy, RetrievalMetrics Metrics)>(retrievers.Length);
foreach (var retriever in retrievers)
{
    var samples = new List<(GoldenItem Gold, IReadOnlyList<RetrievalResult> Hits)>(gold.Count);
    foreach (var item in gold)
    {
        var hits = await retriever.RetrieveAsync(item.Query, K).ConfigureAwait(false);
        samples.Add((item, hits));
    }
    rows.Add((retriever.Strategy, RetrievalEvaluator.Evaluate(samples, K)));
}

// --- Comparison table. --------------------------------------------------------
Console.WriteLine();
Console.WriteLine(
    $"{"retriever",-26} {$"recall@{K}",10} {$"prec@{K}",10} {"MRR",8} {$"nDCG@{K}",10}");
Console.WriteLine(new string('-', 68));
foreach (var (strategy, m) in rows)
{
    Console.WriteLine(
        $"{strategy,-26} {m.RecallAtK,10:P1} {m.PrecisionAtK,10:P1} {m.Mrr,8:F3} {m.NdcgAtK,10:P1}");
}
Console.WriteLine();
Console.WriteLine($"Recall is measured at k={K} over {gold.Count} gold queries, matched on DocumentId.");
Console.WriteLine("Fusing dense + sparse recovers hits either leg misses, so hybrid leads on recall.");
return 0;

static async Task<bool> IsOllamaReachableAsync(Uri endpoint)
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var response = await http.GetAsync(endpoint).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }
    catch (HttpRequestException)
    {
        return false;
    }
    catch (TaskCanceledException)
    {
        return false;
    }
}
