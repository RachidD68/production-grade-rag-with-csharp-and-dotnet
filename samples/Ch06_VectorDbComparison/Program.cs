// Chapter 6 — Vector Database Comparison.
//
// Indexes the SAME ~1,000-chunk synthetic SmartDocs corpus into every
// available IVectorStore backend (InMemory always; Qdrant + Azure AI Search
// when configured), runs a fixed gold query set through the common port, and
// prints per-store p50/p95 query latency and mean recall@10 against the gold
// set. InMemory is exact brute force, so it is the recall ceiling (1.0);
// approximate ANN stores trade a little recall for speed at scale.
//
// Run (InMemory only — no Docker needed):
//   dotnet run --project samples/Ch06_VectorDbComparison
//
// Run with Qdrant (native qdrant.exe on localhost:6334 — see docs/local-setup.md):
//   RUN_QDRANT_INTEGRATION=1 dotnet run --project samples/Ch06_VectorDbComparison
//
// Run with Azure AI Search (set all three):
//   AZURE_SEARCH_ENDPOINT=https://<svc>.search.windows.net
//   AZURE_SEARCH_API_KEY=<admin-key>
//   AZURE_SEARCH_INDEX=ch06-comparison

using System.Diagnostics;
using Azure;
using Qdrant.Client;
using RagInDotNet.Samples.Ch06_VectorDbComparison;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Retrieval.VectorStores;

Console.WriteLine("=== Ch06: Vector DB Comparison ===");

var corpus = Corpus.Build();
var goldQueries = Corpus.BuildGoldQueries(corpus);

Console.WriteLine(
    $"Corpus: {corpus.Count} chunks, {Corpus.Dimensions} dims | " +
    $"Gold queries: {goldQueries.Count}, recall@{Corpus.GoldK}");
Console.WriteLine();

var rows = new List<StoreReport>();

// --- InMemory: always runs, exact brute force. ---
rows.Add(await BenchmarkAsync(
    "InMemory",
    () => new InMemoryVectorStore("ch06-comparison"),
    corpus,
    goldQueries));

// --- Qdrant: gated by RUN_QDRANT_INTEGRATION + a reachable instance. ---
if (Environment.GetEnvironmentVariable("RUN_QDRANT_INTEGRATION") == "1")
{
    try
    {
        var collection = $"ch06-comparison-{Guid.NewGuid():N}";
        QdrantClient? client = null;
        var report = await BenchmarkAsync(
            "Qdrant",
            () =>
            {
                client = new QdrantClient("localhost", 6334);
                return new QdrantVectorStore(client, collection, Corpus.Dimensions);
            },
            corpus,
            goldQueries);
        rows.Add(report);
        if (client is not null)
        {
            try { await client.DeleteCollectionAsync(collection); } catch { /* best-effort cleanup */ }
            client.Dispose();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Qdrant] Skipped — {ex.Message}");
    }
}
else
{
    Console.WriteLine("[Qdrant] Skipped — set RUN_QDRANT_INTEGRATION=1 and start a local Qdrant to include it.");
}

// --- Azure AI Search: gated by endpoint + key + index env vars. ---
var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_SEARCH_ENDPOINT");
var azureKey = Environment.GetEnvironmentVariable("AZURE_SEARCH_API_KEY");
var azureIndex = Environment.GetEnvironmentVariable("AZURE_SEARCH_INDEX");
if (!string.IsNullOrWhiteSpace(azureEndpoint) && !string.IsNullOrWhiteSpace(azureKey) && !string.IsNullOrWhiteSpace(azureIndex))
{
    try
    {
        rows.Add(await BenchmarkAsync(
            "AzureAISearch",
            () => new AzureAiSearchVectorStore(
                new Uri(azureEndpoint),
                azureIndex,
                Corpus.Dimensions,
                new AzureKeyCredential(azureKey)),
            corpus,
            goldQueries,
            // Azure indexing is eventually consistent; give it a moment to settle.
            postIndexDelay: TimeSpan.FromSeconds(3)));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[AzureAISearch] Skipped — {ex.Message}");
    }
}
else
{
    Console.WriteLine("[AzureAISearch] Skipped — set AZURE_SEARCH_ENDPOINT / AZURE_SEARCH_API_KEY / AZURE_SEARCH_INDEX to include it.");
}

Console.WriteLine();
Console.WriteLine($"{"Store",-16} {"recall@10",10} {"p50 (ms)",10} {"p95 (ms)",10}");
Console.WriteLine(new string('-', 50));
foreach (var r in rows)
{
    Console.WriteLine($"{r.Name,-16} {r.MeanRecall,10:P1} {r.P50Ms,10:F2} {r.P95Ms,10:F2}");
}
Console.WriteLine();
Console.WriteLine("Done.");
return 0;

static async Task<StoreReport> BenchmarkAsync(
    string name,
    Func<IVectorStore> factory,
    IReadOnlyList<EmbeddedChunk> corpus,
    IReadOnlyList<GoldQuery> goldQueries,
    TimeSpan? postIndexDelay = null)
{
    var store = factory();
    await store.EnsureCollectionExistsAsync();

    // Index in batches so large corpora don't hit request-size limits.
    foreach (var batch in corpus.Chunk(200))
    {
        await store.UpsertAsync(batch);
    }
    if (postIndexDelay is { } delay)
    {
        await Task.Delay(delay);
    }

    var latencies = new List<double>(goldQueries.Count);
    var recalls = new List<double>(goldQueries.Count);
    foreach (var query in goldQueries)
    {
        var sw = Stopwatch.StartNew();
        var hits = await store.SearchAsync(query.Vector, Corpus.GoldK);
        sw.Stop();
        latencies.Add(sw.Elapsed.TotalMilliseconds);
        recalls.Add(ComparisonMetrics.RecallAtK(hits.Select(h => h.Chunk.ChunkId), query.Gold));
    }

    return new StoreReport(
        name,
        ComparisonMetrics.Mean(recalls),
        ComparisonMetrics.Percentile(latencies, 50),
        ComparisonMetrics.Percentile(latencies, 95));
}

internal sealed record StoreReport(string Name, double MeanRecall, double P50Ms, double P95Ms);
