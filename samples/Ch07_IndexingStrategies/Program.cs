// Chapter 7 — Indexing Strategies.
//
// Demonstrates full-chunk vs parent-child indexing approaches.
// A sample document (3 paragraphs) is indexed both ways, then a
// fine-grained query is used to show how parent-child retrieval
// surfaces the right parent context while full-chunk may miss it.
//
// Run:
//   dotnet run --project samples/Ch07_IndexingStrategies

using System.Numerics.Tensors;

// --- Sample document ---

string[] paragraphs =
[
    "The company's remote work policy allows employees to work from home up to three days per week. " +
    "Employees must be available during core hours of 10 AM to 3 PM in their local time zone. " +
    "All remote workers must use the company VPN for accessing internal systems.",

    "Performance reviews are conducted quarterly using the OKR framework. " +
    "Each employee sets three to five objectives at the beginning of the quarter. " +
    "Managers provide written feedback within two weeks of the quarter ending.",

    "The equipment stipend is $1,500 per year for home office setup. " +
    "Approved items include monitors, keyboards, ergonomic chairs, and standing desks. " +
    "Receipts must be submitted through the expense portal within 30 days of purchase.",
];

Console.WriteLine("=== Ch07: Indexing Strategies ===");
Console.WriteLine();

// --- Full-chunk indexing ---

Console.WriteLine("--- Full-Chunk Indexing (paragraph = chunk) ---");
var fullChunks = IndexFullChunks(paragraphs);
Console.WriteLine($"Indexed {fullChunks.Count} chunks");

// --- Parent-child indexing ---

Console.WriteLine();
Console.WriteLine("--- Parent-Child Indexing (sentence = child, paragraph = parent) ---");
var parentChunks = IndexParentChild(paragraphs);
var totalChildren = parentChunks.Sum(p => p.Children.Count);
Console.WriteLine($"Indexed {parentChunks.Count} parents, {totalChildren} children");

// --- Query ---

const string query = "What is the equipment stipend amount?";
Console.WriteLine();
Console.WriteLine($"Query: \"{query}\"");

// Simulate a query embedding biased toward financial/stipend content.
var queryEmbedding = BagOfWordsEmbedding(query);

// Full-chunk retrieval.
Console.WriteLine();
Console.WriteLine("--- Full-Chunk Results (top 1) ---");
var fullResult = fullChunks
    .OrderByDescending(c => CosineSim(queryEmbedding.Span, c.Embedding.Span))
    .First();
Console.WriteLine($"  [{fullResult.Id}] {Truncate(fullResult.Content, 80)}");

// Parent-child retrieval: search children, return parent.
Console.WriteLine();
Console.WriteLine("--- Parent-Child Results (search children, return parent) ---");
var allChildren = parentChunks.SelectMany(p => p.Children);
var bestChild = allChildren
    .OrderByDescending(c => CosineSim(queryEmbedding.Span, c.Embedding.Span))
    .First();
var bestParent = parentChunks.First(p => p.Id == bestChild.ParentId);
Console.WriteLine($"  Best child: [{bestChild.Id}] \"{Truncate(bestChild.Content, 60)}\"");
Console.WriteLine($"  Returned parent: [{bestParent.Id}] \"{Truncate(bestParent.Content, 80)}\"");
Console.WriteLine($"  Parent has {bestParent.Children.Count} children providing full context.");

Console.WriteLine();
Console.WriteLine("Key insight: Parent-child indexing matches on fine-grained sentences");
Console.WriteLine("but returns the full paragraph for richer LLM context.");
return;

// --- Indexers ---

static List<FullChunk> IndexFullChunks(string[] paragraphs) =>
    paragraphs.Select((p, i) => new FullChunk(
        Id: $"full-{i}",
        Content: p,
        Embedding: BagOfWordsEmbedding(p)
    )).ToList();

static List<ParentChunk> IndexParentChild(string[] paragraphs) =>
    paragraphs.Select((p, pi) =>
    {
        var sentences = p.Split(". ", StringSplitOptions.RemoveEmptyEntries);
        var children = sentences.Select((s, si) => new ChildChunk(
            Id: $"child-{pi}-{si}",
            ParentId: $"parent-{pi}",
            Content: s.TrimEnd('.') + ".",
            Embedding: BagOfWordsEmbedding(s)
        )).ToList();

        return new ParentChunk($"parent-{pi}", p, children);
    }).ToList();

// --- Utility ---

static ReadOnlyMemory<float> BagOfWordsEmbedding(string text)
{
    // Simplified bag-of-words embedding using word hashing.
    // Deterministic, no external model needed.
    const int dims = 64;
    var vec = new float[dims];
    var words = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    foreach (var word in words)
    {
        var hash = word.GetHashCode(StringComparison.Ordinal);
        var idx = Math.Abs(hash) % dims;
        vec[idx] += 1.0f;
    }

    // Normalize.
    var mag = MathF.Sqrt(vec.Sum(v => v * v));
    if (mag > 0)
    {
        for (var i = 0; i < dims; i++)
        {
            vec[i] /= mag;
        }
    }

    return vec;
}

static float CosineSim(ReadOnlySpan<float> a, ReadOnlySpan<float> b) =>
    TensorPrimitives.CosineSimilarity(a, b);

static string Truncate(string s, int max) =>
    s.Length <= max ? s : s[..max] + "...";

// --- Domain types (must follow top-level statements) ---

/// <summary>A chunk in the full-chunk index (each paragraph = one chunk).</summary>
sealed record FullChunk(string Id, string Content, ReadOnlyMemory<float> Embedding);

/// <summary>A parent chunk in parent-child indexing (paragraph-level).</summary>
sealed record ParentChunk(string Id, string Content, IReadOnlyList<ChildChunk> Children);

/// <summary>A child chunk (sentence-level) pointing back to its parent.</summary>
sealed record ChildChunk(string Id, string ParentId, string Content, ReadOnlyMemory<float> Embedding);
