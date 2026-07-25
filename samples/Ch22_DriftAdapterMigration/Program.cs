// Chapter 22 — Drift-Adapter Migration.
//
// Demonstrates the Drift-Adapter as a cheaper alternative to a full re-embed
// when an embedding model changes. A small SmartDocs corpus is indexed with an
// "old" embedder. The provider then ships a "new" model whose geometry is the old
// space under a fixed rotation (the everyday minor-version drift). Queries are
// embedded with the new model, so they no longer line up with the old index.
//
// We train a DriftAdapter on a handful of (old, new) vector pairs — a real
// Orthogonal-Procrustes solve via MathNet.Numerics — and apply it to the old
// index vectors at query time, lifting them into the new model's space without
// re-embedding the corpus. The recall table shows recall@1 recovering from the
// un-adapted baseline toward the full-re-embed ceiling.
//
// Deterministic and offline: both embedders are stable FNV-1a bag-of-words stubs,
// so the printed numbers reproduce run to run with no model or API key.
//
// Run:
//   dotnet run --project samples/Ch22_DriftAdapterMigration

using System.Text.RegularExpressions;
using SmartDocs.Operations;

const int Dimensions = 64;
const int K = 1;

Console.WriteLine("=== Ch22: Drift-Adapter Migration (offline) ===");
Console.WriteLine();

// --- Corpus: 8 short SmartDocs snippets, each its own "document". --------------
string[] corpus =
[
    "Employees receive twenty paid vacation days per fiscal year accrued monthly.",
    "Sick leave is unlimited for employees in good standing with manager approval.",
    "The retrieval service embeds the query then searches the vector index for matches.",
    "Vector databases store dense embeddings and rank candidates by cosine similarity.",
    "Quarterly financial reports are filed with the finance department by each office.",
    "The Casablanca office handles regional contracts and legal document review.",
    "Re-chunking shifts boundaries so old chunks must be deleted before re-indexing.",
    "A drift adapter learns a rotation from an old embedding model to a new one.",
];

// Five gold queries, each mapped to the index of its single relevant document.
(string Query, int Relevant)[] gold =
[
    ("how many vacation days do employees get", 0),
    ("what is the sick leave policy", 1),
    ("how does the retrieval service find matches", 2),
    ("where are quarterly financial reports filed", 4),
    ("what does a drift adapter learn", 7),
];

// --- Two embedders: old, and new = old-space under a fixed rotation. -----------
var rotation = BuildRotation(Dimensions);

float[] EmbedOld(string text) => BagOfWords(text, Dimensions);
float[] EmbedNew(string text) => Normalize(Multiply(rotation, BagOfWords(text, Dimensions)));

// --- Index the corpus with the OLD embedder. -----------------------------------
var oldIndex = corpus.Select(EmbedOld).ToArray();

// --- Train the Drift-Adapter on (old, new) pairs from the corpus. --------------
// In production the pairs come from re-embedding a sample of documents with both
// models; here every corpus item is a pair.
var adapter = new DriftAdapter();
adapter.Train(
    corpus.Select(t => new ReadOnlyMemory<float>(EmbedOld(t))).ToArray(),
    corpus.Select(t => new ReadOnlyMemory<float>(EmbedNew(t))).ToArray());

// Adapt the old index into the new model's space, once, at migration time.
var adaptedIndex = oldIndex.Select(v => adapter.Apply(v)).ToArray();

// The full-re-embed ceiling: what recall would be if we re-embedded everything.
var reembeddedIndex = corpus.Select(EmbedNew).ToArray();

// --- Evaluate recall@K for three strategies. -----------------------------------
// Queries are always embedded with the NEW model (the migration has happened).
var newQueries = gold.Select(g => EmbedNew(g.Query)).ToArray();

double baseline = Recall(newQueries, oldIndex, gold, K);       // new query vs old index (no fix)
double adapted = Recall(newQueries, adaptedIndex, gold, K);    // new query vs adapted old index
double reembed = Recall(newQueries, reembeddedIndex, gold, K); // new query vs fully re-embedded index

Console.WriteLine($"Corpus: {corpus.Length} documents | Gold queries: {gold.Length} | recall@{K}");
Console.WriteLine();
Console.WriteLine($"{"strategy",-34}{$"recall@{K}",10}");
Console.WriteLine(new string('-', 44));
Console.WriteLine($"{"new query vs OLD index (no fix)",-34}{baseline,10:P0}");
Console.WriteLine($"{"new query vs DRIFT-ADAPTED index",-34}{adapted,10:P0}");
Console.WriteLine($"{"new query vs FULL RE-EMBED (ceiling)",-34}{reembed,10:P0}");
Console.WriteLine();
Console.WriteLine(
    adapted > baseline
        ? $"The Drift-Adapter recovers recall from {baseline:P0} to {adapted:P0} without re-embedding the corpus."
        : "No recovery observed (check the rotation / pairs).");
return 0;

// --- Helpers -------------------------------------------------------------------

static double Recall(float[][] queries, float[][] index, (string Query, int Relevant)[] gold, int k)
{
    int hits = 0;
    for (int q = 0; q < queries.Length; q++)
    {
        var topK = Enumerable.Range(0, index.Length)
            .Select(i => (Doc: i, Score: Cosine(queries[q], index[i])))
            .OrderByDescending(x => x.Score)
            .Take(k)
            .Select(x => x.Doc)
            .ToHashSet();
        if (topK.Contains(gold[q].Relevant))
        {
            hits++;
        }
    }

    return (double)hits / queries.Length;
}

// Deterministic FNV-1a bag-of-words embedder (same recipe as the Ch08 sample).
static float[] BagOfWords(string text, int dims)
{
    var vec = new float[dims];
    foreach (Match m in WordRegex().Matches(text.ToLowerInvariant()))
    {
        vec[(int)(Fnv1a(m.Value) % (uint)dims)] += 1f;
    }

    return Normalize(vec);
}

// A fixed, deterministic rotation: a product of Givens rotations over dimension
// pairs by a constant angle, seeded so the matrix is identical every run.
static float[,] BuildRotation(int dims)
{
    var m = new float[dims, dims];
    for (int i = 0; i < dims; i++)
    {
        m[i, i] = 1f;
    }

    const float Angle = 1.2f; // radians, ~69° — a large drift so the un-adapted baseline breaks
    var c = MathF.Cos(Angle);
    var s = MathF.Sin(Angle);
    for (int i = 0; i + 1 < dims; i += 2)
    {
        // Rotate the (i, i+1) plane.
        var rii = m[i, i];
        var ri1 = m[i, i + 1];
        var r1i = m[i + 1, i];
        var r11 = m[i + 1, i + 1];
        m[i, i] = c * rii - s * r1i;
        m[i, i + 1] = c * ri1 - s * r11;
        m[i + 1, i] = s * rii + c * r1i;
        m[i + 1, i + 1] = s * ri1 + c * r11;
    }

    return m;
}

static float[] Multiply(float[,] matrix, float[] v)
{
    int n = v.Length;
    var result = new float[n];
    for (int i = 0; i < n; i++)
    {
        float acc = 0f;
        for (int j = 0; j < n; j++)
        {
            acc += matrix[i, j] * v[j];
        }

        result[i] = acc;
    }

    return result;
}

static float[] Normalize(float[] v)
{
    var mag = MathF.Sqrt(v.Sum(x => x * x));
    if (mag > 1e-8f)
    {
        for (int i = 0; i < v.Length; i++)
        {
            v[i] /= mag;
        }
    }

    return v;
}

static float Cosine(float[] a, float[] b)
{
    float dot = 0f, ma = 0f, mb = 0f;
    for (int i = 0; i < a.Length; i++)
    {
        dot += a[i] * b[i];
        ma += a[i] * a[i];
        mb += b[i] * b[i];
    }

    var denom = MathF.Sqrt(ma) * MathF.Sqrt(mb);
    return denom > 1e-8f ? dot / denom : 0f;
}

static uint Fnv1a(string s)
{
    uint hash = 2166136261;
    foreach (var ch in s)
    {
        hash ^= ch;
        hash *= 16777619;
    }

    return hash;
}

internal static partial class Program
{
    [GeneratedRegex(@"[a-z0-9][a-z0-9\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
