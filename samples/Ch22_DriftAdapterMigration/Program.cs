// Chapter 22 — Drift Adapter Migration.
//
// Demonstrates embedding drift detection and Procrustes alignment.
// Generates two sets of synthetic embeddings simulating old vs new model
// outputs, applies a known rotation + scaling to simulate drift, then
// uses simplified Procrustes alignment to recover the original space.
// Measures cosine similarity before and after alignment.
//
// Run:
//   dotnet run --project samples/Ch22_DriftAdapterMigration

const int NumVectors = 20;
const int Dimensions = 32;

Console.WriteLine("=== Ch22: Embedding Drift Detection & Procrustes Alignment ===");
Console.WriteLine();

var rng = new Random(42);

// Generate "old model" embeddings (ground truth).
var oldEmbeddings = GenerateNormalizedVectors(rng, NumVectors, Dimensions);

// Simulate "new model" drift: apply rotation + scaling.
// This mimics what happens when a provider updates their embedding model.
var rotationMatrix = GenerateRotationMatrix(rng, Dimensions, angleDegrees: 15.0);
const float ScaleFactor = 1.08f; // 8% magnitude drift
var newEmbeddings = ApplyDrift(oldEmbeddings, rotationMatrix, ScaleFactor);

// --- Measure drift ---

Console.WriteLine("--- Drift Detection ---");
var prealignmentSims = MeasurePairwiseSimilarity(oldEmbeddings, newEmbeddings);
Console.WriteLine($"  Mean cosine similarity (old vs drifted): {prealignmentSims.Mean:F4}");
Console.WriteLine($"  Min cosine similarity:                   {prealignmentSims.Min:F4}");
Console.WriteLine($"  Max cosine similarity:                   {prealignmentSims.Max:F4}");
Console.WriteLine($"  Drift detected: {(prealignmentSims.Mean < 0.95 ? "YES" : "NO")} (threshold: 0.95)");
Console.WriteLine();

// --- Procrustes alignment ---

Console.WriteLine("--- Procrustes Alignment ---");
Console.WriteLine("  Computing alignment transform from anchor pairs...");

// Use first 10 vectors as anchor pairs (known correspondences).
const int AnchorCount = 10;
var alignmentTransform = ComputeProcrustesAlignment(
    oldEmbeddings[..AnchorCount],
    newEmbeddings[..AnchorCount],
    Dimensions);

// Apply alignment to ALL new embeddings.
var alignedEmbeddings = ApplyAlignment(newEmbeddings, alignmentTransform, Dimensions);

// --- Measure post-alignment quality ---

var postAlignmentSims = MeasurePairwiseSimilarity(oldEmbeddings, alignedEmbeddings);
Console.WriteLine();
Console.WriteLine("--- Post-Alignment Metrics ---");
Console.WriteLine($"  Mean cosine similarity (old vs aligned): {postAlignmentSims.Mean:F4}");
Console.WriteLine($"  Min cosine similarity:                   {postAlignmentSims.Min:F4}");
Console.WriteLine($"  Max cosine similarity:                   {postAlignmentSims.Max:F4}");
Console.WriteLine($"  Drift resolved: {(postAlignmentSims.Mean >= 0.95 ? "YES" : "NO")}");
Console.WriteLine();

// --- Summary ---

Console.WriteLine("--- Summary ---");
Console.WriteLine($"  Pre-alignment mean similarity:  {prealignmentSims.Mean:F4}");
Console.WriteLine($"  Post-alignment mean similarity: {postAlignmentSims.Mean:F4}");
Console.WriteLine($"  Improvement:                    {postAlignmentSims.Mean - prealignmentSims.Mean:+F4}");
Console.WriteLine();
Console.WriteLine("  Procrustes alignment recovers the original embedding space");
Console.WriteLine("  without re-embedding the entire corpus.");
return;

// --- Implementation ---

static float[][] GenerateNormalizedVectors(Random rng, int count, int dims)
{
    var vectors = new float[count][];
    for (var i = 0; i < count; i++)
    {
        var vec = new float[dims];
        for (var d = 0; d < dims; d++)
        {
            vec[d] = (float)NextGaussian(rng);
        }

        Normalize(vec);
        vectors[i] = vec;
    }

    return vectors;
}

static float[,] GenerateRotationMatrix(Random rng, int dims, double angleDegrees)
{
    // Simplified: apply Givens rotations in pairs of dimensions.
    var matrix = new float[dims, dims];

    // Start with identity.
    for (var i = 0; i < dims; i++)
    {
        matrix[i, i] = 1.0f;
    }

    // Apply rotation in pairs of dimensions.
    var angleRad = angleDegrees * Math.PI / 180.0;

    for (var i = 0; i < dims - 1; i += 2)
    {
        var perturbation = (float)(rng.NextDouble() * 0.3 * angleRad);
        var c = (float)Math.Cos(angleRad + perturbation);
        var s = (float)Math.Sin(angleRad + perturbation);
        matrix[i, i] = c;
        matrix[i, i + 1] = -s;
        matrix[i + 1, i] = s;
        matrix[i + 1, i + 1] = c;
    }

    return matrix;
}

static float[][] ApplyDrift(float[][] vectors, float[,] rotation, float scale)
{
    var dims = vectors[0].Length;
    var result = new float[vectors.Length][];

    for (var v = 0; v < vectors.Length; v++)
    {
        var transformed = new float[dims];
        for (var i = 0; i < dims; i++)
        {
            var sum = 0f;
            for (var j = 0; j < dims; j++)
            {
                sum += rotation[i, j] * vectors[v][j];
            }

            transformed[i] = sum * scale;
        }

        Normalize(transformed);
        result[v] = transformed;
    }

    return result;
}

static float[,] ComputeProcrustesAlignment(float[][] source, float[][] target, int dims)
{
    // Simplified Procrustes: compute the best rotation mapping source -> target.
    // M = target^T * source, then iterative normalization to approximate orthogonal.

    var m = new float[dims, dims];

    // M = sum of outer products: target_i * source_i^T
    for (var k = 0; k < source.Length; k++)
    {
        for (var i = 0; i < dims; i++)
        {
            for (var j = 0; j < dims; j++)
            {
                m[i, j] += target[k][i] * source[k][j];
            }
        }
    }

    // Approximate orthogonal Procrustes via iterative row/column normalization.
    for (var iter = 0; iter < 20; iter++)
    {
        // Normalize rows.
        for (var i = 0; i < dims; i++)
        {
            var rowMag = 0f;
            for (var j = 0; j < dims; j++)
            {
                rowMag += m[i, j] * m[i, j];
            }

            rowMag = MathF.Sqrt(rowMag);
            if (rowMag > 1e-8f)
            {
                for (var j = 0; j < dims; j++)
                {
                    m[i, j] /= rowMag;
                }
            }
        }

        // Normalize columns.
        for (var j = 0; j < dims; j++)
        {
            var colMag = 0f;
            for (var i = 0; i < dims; i++)
            {
                colMag += m[i, j] * m[i, j];
            }

            colMag = MathF.Sqrt(colMag);
            if (colMag > 1e-8f)
            {
                for (var i = 0; i < dims; i++)
                {
                    m[i, j] /= colMag;
                }
            }
        }
    }

    return m;
}

static float[][] ApplyAlignment(float[][] vectors, float[,] transform, int dims)
{
    var result = new float[vectors.Length][];

    for (var v = 0; v < vectors.Length; v++)
    {
        var aligned = new float[dims];
        for (var i = 0; i < dims; i++)
        {
            var sum = 0f;
            for (var j = 0; j < dims; j++)
            {
                sum += transform[i, j] * vectors[v][j];
            }

            aligned[i] = sum;
        }

        Normalize(aligned);
        result[v] = aligned;
    }

    return result;
}

static (float Mean, float Min, float Max) MeasurePairwiseSimilarity(float[][] a, float[][] b)
{
    var sims = new float[a.Length];
    for (var i = 0; i < a.Length; i++)
    {
        sims[i] = CosineSimilarity(a[i], b[i]);
    }

    return (sims.Average(), sims.Min(), sims.Max());
}

static float CosineSimilarity(float[] a, float[] b)
{
    var dot = 0f;
    var magA = 0f;
    var magB = 0f;
    for (var i = 0; i < a.Length; i++)
    {
        dot += a[i] * b[i];
        magA += a[i] * a[i];
        magB += b[i] * b[i];
    }

    return dot / (MathF.Sqrt(magA) * MathF.Sqrt(magB));
}

static void Normalize(float[] vec)
{
    var mag = MathF.Sqrt(vec.Sum(v => v * v));
    if (mag > 1e-8f)
    {
        for (var i = 0; i < vec.Length; i++)
        {
            vec[i] /= mag;
        }
    }
}

static double NextGaussian(Random rng)
{
    // Box-Muller transform.
    var u1 = 1.0 - rng.NextDouble();
    var u2 = rng.NextDouble();
    return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
}
