// Chapter 7 — Indexing Strategies.
//
// A recall@k comparison harness for the four real indexing strategies in
// SmartDocs.Ingestion.Indexing: chunk, sub-chunk, summary, and query. Each
// strategy projects a DocumentChunk into one or more "embedding inputs"; the
// harness embeds those inputs, upserts them into a fresh in-memory vector
// store, runs a fixed gold query set, and reports recall@k alongside the cost
// signals that distinguish the strategies — vectors indexed, index-time LLM
// calls, and wall-clock.
//
// Deterministic and offline by default: a bag-of-words embedder and a stub
// IChatClient stand in for a real model so the run is reproducible in CI with
// no key. Set CH07_USE_OLLAMA=1 (optionally OLLAMA_ENDPOINT) to swap the
// embedder for a live Ollama model; the chat client stays stubbed so question
// and summary text remain deterministic.
//
// Run (offline, default):
//   dotnet run --project samples/Ch07_IndexingStrategies
//
// Run with a real embedder:
//   CH07_USE_OLLAMA=1 dotnet run --project samples/Ch07_IndexingStrategies

using System.Diagnostics;
using Microsoft.Extensions.AI;
using OllamaSharp;
using RagInDotNet.Samples.Ch07_IndexingStrategies;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Evaluation;
using SmartDocs.Ingestion.Indexing;
using SmartDocs.Retrieval.VectorStores;

Console.WriteLine("=== Ch07: Indexing Strategies — recall@k comparison ===");
Console.WriteLine();

var chunks = Corpus.BuildChunks();
var gold = Corpus.BuildGoldQueries();
const int K = Corpus.GoldK;

// --- Embedder: deterministic bag-of-words by default; Ollama on request. -----
var chat = new CountingStubChatClient();
IEmbeddingGenerator<string, Embedding<float>> embedder = new BagOfWordsEmbeddingGenerator();
var embeddingModelName = "bag-of-words-256";

if (Environment.GetEnvironmentVariable("CH07_USE_OLLAMA") == "1")
{
    var endpoint = new Uri(
        Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434");
    const string OllamaEmbeddingModel = "nomic-embed-text";
    if (await IsOllamaReachableAsync(endpoint).ConfigureAwait(false))
    {
        embedder = new OllamaApiClient(endpoint, OllamaEmbeddingModel);
        embeddingModelName = OllamaEmbeddingModel;
        Console.WriteLine($"Embedder: Ollama '{OllamaEmbeddingModel}' at {endpoint}.");
    }
    else
    {
        Console.WriteLine($"CH07_USE_OLLAMA=1 but Ollama is not reachable at {endpoint}.");
        Console.WriteLine("  Falling back to the deterministic bag-of-words embedder.");
    }
}
else
{
    Console.WriteLine("Embedder: deterministic bag-of-words (offline). Set CH07_USE_OLLAMA=1 for Ollama.");
}

Console.WriteLine(
    $"Corpus: {chunks.Count} chunks across {chunks.Select(c => c.DocumentId).Distinct().Count()} documents | " +
    $"Gold queries: {gold.Count}, recall@{K}");
Console.WriteLine();

// --- The four real strategies. ----------------------------------------------
// Each is constructed once and run directly for the comparison. The builder
// below shows the routing-by-extension API the chapter teaches; the comparison
// loop then exercises each strategy in isolation so the per-strategy numbers
// are clean.
var strategies = new IIndexingStrategy[]
{
    new ChunkIndexingStrategy(),
    new SubChunkIndexingStrategy(),
    new SummaryIndexingStrategy(chat),
    new QueryIndexingStrategy(chat, questionsPerChunk: 3),
};

// The production wiring readers will recognize: pick a strategy per document
// type, falling back to plain chunk indexing. Shown once for fidelity to the
// chapter; the measured comparison below runs each strategy on its own.
var pipeline = new IndexingPipelineBuilder()
    .ForDocumentType(".md", new QueryIndexingStrategy(chat))
    .ForDocumentType(".pdf", new SummaryIndexingStrategy(chat))
    .Default(new ChunkIndexingStrategy())
    .Build();
_ = pipeline; // referenced for illustration; the comparison uses the strategies directly.

var reports = new List<StrategyReport>(strategies.Length);
foreach (var strategy in strategies)
{
    reports.Add(await EvaluateAsync(strategy, chunks, gold, embedder, embeddingModelName, chat, K)
        .ConfigureAwait(false));
}

// --- Comparison table. ------------------------------------------------------
Console.WriteLine();
Console.WriteLine(
    $"{"strategy",-10} {"vectors",8} {"LLM calls",10} {$"recall@{K}",10} {"wall (ms)",10}");
Console.WriteLine(new string('-', 52));
foreach (var r in reports)
{
    Console.WriteLine(
        $"{r.Strategy,-10} {r.VectorsIndexed,8} {r.LlmCalls,10} {r.RecallAtK,10:P1} {r.WallClockMs,10:F1}");
}
Console.WriteLine();
Console.WriteLine($"Recall is measured at k={K} over {gold.Count} gold queries; vectors are the embedding");
Console.WriteLine("inputs each strategy produced (query indexing fans one chunk out to N rows).");
return 0;

// --- Harness ----------------------------------------------------------------

// Run one strategy end-to-end: index every chunk into embedding inputs, embed
// each input, upsert into a fresh store, query the gold set, and score recall@k.
static async Task<StrategyReport> EvaluateAsync(
    IIndexingStrategy strategy,
    IReadOnlyList<DocumentChunk> chunks,
    IReadOnlyList<GoldenItem> gold,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    string embeddingModelName,
    CountingStubChatClient chat,
    int k)
{
    chat.Reset();
    var store = new InMemoryVectorStore($"ch07-{strategy.Strategy}");
    await store.EnsureCollectionExistsAsync().ConfigureAwait(false);

    var sw = Stopwatch.StartNew();

    // Index: project each chunk into IndexedItems, embed the key text, upsert.
    var vectorsIndexed = 0;
    foreach (var chunk in chunks)
    {
        await foreach (var item in strategy.IndexAsync(chunk).ConfigureAwait(false))
        {
            var vector = await EmbedAsync(embedder, item.EmbedText).ConfigureAwait(false);
            await store.UpsertAsync(
                [new EmbeddedChunk(item.Payload, vector, embeddingModelName)]).ConfigureAwait(false);
            vectorsIndexed++;
        }
    }

    // Query: embed each gold query, search top-k, pair hits with the gold item.
    var samples = new List<(GoldenItem Gold, IReadOnlyList<RetrievalResult> Hits)>(gold.Count);
    foreach (var item in gold)
    {
        var queryVector = await EmbedAsync(embedder, item.Query).ConfigureAwait(false);
        var hits = await store.SearchAsync(queryVector, k).ConfigureAwait(false);
        samples.Add((item, hits));
    }

    sw.Stop();

    // Reuse the production metric — recall@k matches on DocumentId.
    var metrics = RetrievalEvaluator.Evaluate(samples, k);

    return new StrategyReport(
        Strategy: strategy.Strategy,
        VectorsIndexed: vectorsIndexed,
        LlmCalls: chat.CallCount,
        RecallAtK: metrics.RecallAtK,
        WallClockMs: sw.Elapsed.TotalMilliseconds);
}

// GenerateAsync returns a collection; take [0].Vector (the MEAI convention).
static async Task<ReadOnlyMemory<float>> EmbedAsync(
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    string text)
{
    var embeddings = await embedder.GenerateAsync([text]).ConfigureAwait(false);
    return embeddings[0].Vector;
}

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

/// <summary>One row of the strategy comparison table.</summary>
internal sealed record StrategyReport(
    string Strategy,
    int VectorsIndexed,
    int LlmCalls,
    double RecallAtK,
    double WallClockMs);
