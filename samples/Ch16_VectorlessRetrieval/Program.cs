// Chapter 16 — Vectorless Retrieval.
//
// An offline, deterministic recall comparison of three retrievers over one
// hand-shaped GDPR-like corpus:
//   * structural — deterministic id lookup + cross-reference following
//     (SmartDocs.Retrieval.Vectorless.StructuralRetriever),
//   * vector     — dense search over the same articles chunked one-per-article
//     (DenseRetriever + InMemoryVectorStore, FNV bag-of-words embedder),
//   * hybrid     — HybridRouter routes by query kind: identifier -> structural,
//     topic -> vector, mixed -> RRF fusion of both (Ch 8).
//
// A ~16-query eval set is pre-tagged identifier / topic / both. The printed
// table shows structural winning outright on identifier queries (exact lookup),
// vector pulling its weight on topic queries, and the hybrid router taking the
// best of each. Fully offline and deterministic: a stable FNV-1a bag-of-words
// embedder stands in for a real model (never string.GetHashCode) and an offline
// identifier extractor stands in for the LLM, so the numbers reproduce in CI.
//
// Run:
//   dotnet run --project samples/Ch16_VectorlessRetrieval

using Microsoft.Extensions.Logging.Abstractions;
using RagInDotNet.Samples.Ch16_VectorlessRetrieval;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.Vectorless;
using SmartDocs.Retrieval.VectorStores;
using SmartDocs.Routing;

Console.WriteLine("=== Ch16: Vectorless Retrieval — structural / vector / hybrid recall ===");
Console.WriteLine();

const int K = 5;

// --- Structural index: parse the corpus into an id-linked tree. --------------
var index = DocumentStructureParser.Parse(Corpus.Title, Corpus.Markdown);
var articleNodes = index.AllNodes().Where(n => n.Id.StartsWith("Art", StringComparison.Ordinal)).ToList();
Console.WriteLine($"Corpus: {articleNodes.Count} articles | Eval set: {Corpus.EvalSet.Count} queries (recall@{K})");
Console.WriteLine(
    $"Cross-references resolved: " +
    string.Join(", ", articleNodes
        .Where(n => n.CrossReferences.Count > 0)
        .Select(n => $"{n.Id}->[{string.Join(",", n.CrossReferences)}]")));
Console.WriteLine();

// --- Vector index: embed each article as one chunk into the in-memory store. --
var embedder = new BagOfWordsEmbeddingGenerator();
var embeddingService = new EmbeddingService(
    embedder, "bag-of-words-256", 256, NullLogger<EmbeddingService>.Instance);

var store = new InMemoryVectorStore("ch16-eval");
await store.EnsureCollectionExistsAsync().ConfigureAwait(false);
foreach (var node in articleNodes)
{
    // Chunk id == node id, so both legs match the gold id the same way.
    var meta = new DocumentMetadata(
        node.Id, "gdpr", "Legal", "All", "Public", "Contract",
        2026, "gdpr", new DateOnly(2026, 1, 1), node.Title);
    var text = $"{node.Title}\n{node.Text}";
    var chunk = new DocumentChunk(node.Id, "gdpr", 0, text, 0, text.Length, meta);
    var embedded = await embeddingService.EmbedAsync(chunk).ConfigureAwait(false);
    await store.UpsertAsync([embedded]).ConfigureAwait(false);
}

// --- The three retrievers. ----------------------------------------------------
IRetriever vector = new DenseRetriever(embeddingService, store);

// Structural: offline identifier extraction, with the vector leg as the topic
// fallback so topic queries still return something.
IRetriever structural = new StructuralRetriever(index, new OfflineIdentifierChatClient(), topicFallback: vector);

// Hybrid: classify the query, then route to structural / vector / fused(RRF).
IRetriever fused = new HybridRetriever(structural, vector);
IRetriever hybrid = new HybridRouter(new KeywordRouteClassifier(), structural, vector, fused);

// --- Evaluate recall@K, overall and per query kind. ---------------------------
var retrievers = new (string Name, IRetriever Retriever)[]
{
    ("structural", structural),
    ("vector", vector),
    ("hybrid", hybrid),
};

var kinds = new[] { QueryKind.Identifier, QueryKind.Topic, QueryKind.Both };
var rows = new List<(string Name, double Overall, Dictionary<QueryKind, double> ByKind)>();

foreach (var (name, retriever) in retrievers)
{
    var byKind = new Dictionary<QueryKind, double>();
    foreach (var kind in kinds)
    {
        var queries = Corpus.EvalSet.Where(q => q.Kind == kind).ToList();
        var hitCount = 0;
        foreach (var q in queries)
        {
            var hits = await retriever.RetrieveAsync(q.Query, K).ConfigureAwait(false);
            if (hits.Any(h => string.Equals(h.Chunk.ChunkId, q.GoldId, StringComparison.Ordinal)))
            {
                hitCount++;
            }
        }
        byKind[kind] = queries.Count == 0 ? 0 : (double)hitCount / queries.Count;
    }

    var totalHits = 0;
    foreach (var q in Corpus.EvalSet)
    {
        var hits = await retriever.RetrieveAsync(q.Query, K).ConfigureAwait(false);
        if (hits.Any(h => string.Equals(h.Chunk.ChunkId, q.GoldId, StringComparison.Ordinal)))
        {
            totalHits++;
        }
    }
    rows.Add((name, (double)totalHits / Corpus.EvalSet.Count, byKind));
}

// --- Comparison table. --------------------------------------------------------
Console.WriteLine(
    $"{"retriever",-12} {"identifier",12} {"topic",10} {"both",8} {"overall",10}");
Console.WriteLine(new string('-', 56));
foreach (var (name, overall, byKind) in rows)
{
    Console.WriteLine(
        $"{name,-12} {byKind[QueryKind.Identifier],12:P0} {byKind[QueryKind.Topic],10:P0} " +
        $"{byKind[QueryKind.Both],8:P0} {overall,10:P0}");
}
Console.WriteLine();
Console.WriteLine($"Recall@{K} over {Corpus.EvalSet.Count} pre-tagged queries, matched on node id.");
Console.WriteLine("Structural lookups are exact on identifier queries; the hybrid router gets");
Console.WriteLine("the best of both by routing identifiers to structural and topics to vector.");
return 0;
