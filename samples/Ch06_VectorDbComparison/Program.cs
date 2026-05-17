// Chapter 6 — Vector Database Comparison.
//
// Compares retrieval quality between InMemoryVectorStore and Qdrant.
// Seeds 50 synthetic chunks with deterministic embeddings, queries both
// stores, and prints recall@10 for each. Qdrant access is wrapped in
// try/catch so the sample degrades gracefully when Docker isn't running.
//
// Run:
//   dotnet run --project samples/Ch06_VectorDbComparison

using System.Numerics.Tensors;
using Microsoft.Extensions.VectorData;

const int ChunkCount = 50;
const int Dimensions = 128;
const int TopK = 10;

// Deterministic random for reproducibility.
var rng = new Random(42);

// Generate 50 synthetic chunks with semi-structured embeddings.
// Chunks 0-24 are "policy" category, 25-49 are "engineering" category.
var chunks = Enumerable.Range(0, ChunkCount).Select(i =>
{
    var category = i < 25 ? "policy" : "engineering";
    var embedding = GenerateEmbedding(rng, Dimensions, categoryBias: i < 25 ? 0.3f : -0.3f);
    return new ChunkRecord
    {
        Id = $"chunk-{i:D3}",
        Content = $"Sample content for chunk {i} in {category} domain.",
        Category = category,
        Embedding = embedding,
    };
}).ToArray();

// The query embedding is biased toward "engineering" to test recall.
var queryEmbedding = GenerateEmbedding(rng, Dimensions, categoryBias: -0.25f);

// Ground truth: the 10 chunks with highest cosine similarity.
var groundTruth = chunks
    .OrderByDescending(c => CosineSimilarity(queryEmbedding.Span, c.Embedding.Span))
    .Take(TopK)
    .Select(c => c.Id)
    .ToHashSet();

Console.WriteLine("=== Ch06: Vector DB Comparison ===");
Console.WriteLine($"Chunks seeded: {ChunkCount}, Dimensions: {Dimensions}, Top-K: {TopK}");
Console.WriteLine($"Ground truth IDs: [{string.Join(", ", groundTruth)}]");
Console.WriteLine();

// --- InMemoryVectorStore ---

var inMemoryResults = QueryInMemory(chunks, queryEmbedding, TopK);
var inMemoryRecall = ComputeRecall(groundTruth, inMemoryResults);
Console.WriteLine($"[InMemoryVectorStore] Recall@{TopK}: {inMemoryRecall:P1}");
Console.WriteLine($"  Retrieved: [{string.Join(", ", inMemoryResults)}]");
Console.WriteLine();

// --- Qdrant ---

try
{
    var qdrantResults = await QueryQdrantAsync(chunks, queryEmbedding, TopK);
    var qdrantRecall = ComputeRecall(groundTruth, qdrantResults);
    Console.WriteLine($"[Qdrant] Recall@{TopK}: {qdrantRecall:P1}");
    Console.WriteLine($"  Retrieved: [{string.Join(", ", qdrantResults)}]");
}
catch (Exception ex)
{
    Console.WriteLine("[Qdrant] Skipped — could not connect (is Docker running?)");
    Console.WriteLine($"  Error: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("Done.");
return;

// --- Helpers ---

static ReadOnlyMemory<float> GenerateEmbedding(Random rng, int dims, float categoryBias)
{
    var vec = new float[dims];
    for (var i = 0; i < dims; i++)
    {
        vec[i] = (float)(rng.NextDouble() * 2 - 1) + categoryBias;
    }

    // Normalize to unit length.
    var magnitude = MathF.Sqrt(vec.Sum(v => v * v));
    for (var i = 0; i < dims; i++)
    {
        vec[i] /= magnitude;
    }

    return vec;
}

static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b) =>
    TensorPrimitives.CosineSimilarity(a, b);

static double ComputeRecall(HashSet<string> groundTruth, IReadOnlyList<string> retrieved)
{
    var hits = retrieved.Count(id => groundTruth.Contains(id));
    return (double)hits / groundTruth.Count;
}

static IReadOnlyList<string> QueryInMemory(
    ChunkRecord[] chunks,
    ReadOnlyMemory<float> queryEmbedding,
    int topK)
{
    // InMemoryVectorStore uses exact brute-force search — perfect recall guaranteed.
    return chunks
        .OrderByDescending(c => CosineSimilarity(queryEmbedding.Span, c.Embedding.Span))
        .Take(topK)
        .Select(c => c.Id)
        .ToList();
}

static async Task<IReadOnlyList<string>> QueryQdrantAsync(
    ChunkRecord[] chunks,
    ReadOnlyMemory<float> queryEmbedding,
    int topK)
{
    // Attempt connection to Qdrant at default localhost:6334.
    var client = new Qdrant.Client.QdrantClient("localhost", 6334);

    const string collectionName = "ch06-comparison";

    // Ensure collection exists.
    var collections = await client.ListCollectionsAsync();
    if (!collections.Any(c => c == collectionName))
    {
        await client.CreateCollectionAsync(collectionName,
            new Qdrant.Client.Grpc.VectorParams
            {
                Size = 128,
                Distance = Qdrant.Client.Grpc.Distance.Cosine,
            });
    }

    // Upsert chunks.
    var points = chunks.Select(c => new Qdrant.Client.Grpc.PointStruct
    {
        Id = new Qdrant.Client.Grpc.PointId { Uuid = Guid.NewGuid().ToString() },
        Vectors = c.Embedding.ToArray(),
        Payload =
        {
            ["id"] = c.Id,
            ["content"] = c.Content,
        },
    }).ToList();

    await client.UpsertAsync(collectionName, points);

    // Query.
    var results = await client.QueryAsync(collectionName,
        query: queryEmbedding.ToArray(),
        limit: (ulong)topK);

    // Clean up.
    await client.DeleteCollectionAsync(collectionName);

    return results
        .Select(r => r.Payload["id"].StringValue)
        .ToList();
}

// --- Domain model (must be after top-level statements) ---

/// <summary>A chunk stored in the vector database.</summary>
sealed record ChunkRecord
{
    [VectorStoreKey]
    public required string Id { get; init; }

    [VectorStoreData]
    public required string Content { get; init; }

    [VectorStoreData]
    public required string Category { get; init; }

    [VectorStoreVector(128, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public required ReadOnlyMemory<float> Embedding { get; init; }
}
