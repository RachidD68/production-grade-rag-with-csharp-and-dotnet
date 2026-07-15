using SmartDocs.Core.Documents;

namespace RagInDotNet.Samples.Ch06_VectorDbComparison;

/// <summary>
/// Builds the synthetic SmartDocs corpus and gold query set for the
/// comparison. Embeddings are generated deterministically (seeded RNG,
/// topic-clustered, unit-normalized) so the sample runs offline with no
/// embedding model and the gold set is reproducible run-to-run.
/// </summary>
public static class Corpus
{
    public const int Dimensions = 128;
    public const int ChunkCount = 1_000;
    public const int TopicCount = 20;
    public const int QueryCount = 25;
    public const int GoldK = 10;

    private const int CorpusSeed = 20260606;
    private const int QuerySeed = 424242;

    /// <summary>One topic-clustered chunk plus its embedding.</summary>
    public static IReadOnlyList<EmbeddedChunk> Build()
    {
        var rng = new Random(CorpusSeed);

        // A unit-norm centroid per topic; each chunk is its centroid plus noise.
        var centroids = new float[TopicCount][];
        for (var t = 0; t < TopicCount; t++)
        {
            centroids[t] = Normalize(RandomVector(rng));
        }

        var chunks = new List<EmbeddedChunk>(ChunkCount);
        for (var i = 0; i < ChunkCount; i++)
        {
            var topic = i % TopicCount;
            var vector = Normalize(AddNoise(centroids[topic], rng, scale: 0.35f));

            var documentId = $"doc-{i / 5:D4}";
            var chunkId = $"{documentId}#{i % 5}";
            var meta = new DocumentMetadata(
                Id: documentId,
                Silo: $"silo-{topic % 4}",
                Department: $"dept-{topic}",
                Office: "Montreal",
                ConfidentialityLevel: "Internal",
                DocumentType: "Policy",
                FiscalYear: 2026,
                Author: $"author-{topic}",
                LastModified: new DateOnly(2026, 1, 1).AddDays(i % 365),
                Title: $"Topic {topic} document {i / 5}");

            var chunk = new DocumentChunk(
                ChunkId: chunkId,
                DocumentId: documentId,
                ChunkIndex: i % 5,
                Text: $"Synthetic chunk {i} about topic {topic}.",
                StartCharOffset: 0,
                EndCharOffset: 40,
                Metadata: meta);

            chunks.Add(new EmbeddedChunk(chunk, vector, "synthetic-128"));
        }
        return chunks;
    }

    /// <summary>
    /// A fixed gold query set. For each query we compute the exact top-<see cref="GoldK"/>
    /// by brute-force cosine over the corpus — that is the ground truth every
    /// store's recall@10 is measured against.
    /// </summary>
    public static IReadOnlyList<GoldQuery> BuildGoldQueries(IReadOnlyList<EmbeddedChunk> corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        var rng = new Random(QuerySeed);
        var queries = new List<GoldQuery>(QueryCount);

        for (var q = 0; q < QueryCount; q++)
        {
            var queryVector = Normalize(RandomVector(rng));
            var gold = corpus
                .Select(c => (c.Chunk.ChunkId, Score: Cosine(queryVector, c.Vector.Span)))
                .OrderByDescending(x => x.Score)
                .Take(GoldK)
                .Select(x => x.ChunkId)
                .ToList();
            queries.Add(new GoldQuery(queryVector, gold));
        }
        return queries;
    }

    private static float[] RandomVector(Random rng)
    {
        var v = new float[Dimensions];
        for (var i = 0; i < Dimensions; i++)
        {
            v[i] = (float)(rng.NextDouble() * 2 - 1);
        }
        return v;
    }

    private static float[] AddNoise(float[] baseVector, Random rng, float scale)
    {
        var v = new float[baseVector.Length];
        for (var i = 0; i < baseVector.Length; i++)
        {
            v[i] = baseVector[i] + (float)((rng.NextDouble() * 2 - 1) * scale);
        }
        return v;
    }

    private static float[] Normalize(float[] v)
    {
        double sum = 0;
        foreach (var x in v)
        {
            sum += (double)x * x;
        }
        var mag = Math.Sqrt(sum);
        if (mag == 0)
        {
            return v;
        }
        var result = new float[v.Length];
        for (var i = 0; i < v.Length; i++)
        {
            result[i] = (float)(v[i] / mag);
        }
        return result;
    }

    private static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, ma = 0, mb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            ma += a[i] * a[i];
            mb += b[i] * b[i];
        }
        return ma == 0 || mb == 0 ? 0 : dot / (Math.Sqrt(ma) * Math.Sqrt(mb));
    }
}

/// <summary>A query vector paired with its exact top-k gold chunk ids.</summary>
public sealed record GoldQuery(ReadOnlyMemory<float> Vector, IReadOnlyList<string> Gold);
