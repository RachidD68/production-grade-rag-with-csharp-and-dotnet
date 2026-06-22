// tools/SmartDocs.Reindex — a runnable sketch of the zero-downtime
// embedding-model migration (Ch 25, building on the Ch 22 backfill machinery).
//
// The six stages a real "swap the embedding model under live traffic" runbook
// follows, every one of them offline-runnable here against an InMemoryVectorStore
// and two stub embedders (old vs new model):
//
//   1. Shadow index   — stand up a second collection beside the live one.
//   2. Dual-write     — new ingests/updates land in BOTH indexes (no gap forms).
//   3. Backfill       — re-embed the existing corpus into the shadow with the new
//                       model (Ch 22 BackfillScheduler, shadow-then-cutover mode).
//   4. Eval-gate      — compare the shadow's retrieval quality to a baseline; only
//                       a non-regressing shadow is allowed to go live.
//   5. Atomic cutover — flip reads to the shadow in one step.
//   6. Keep one cycle — retain the old index for one cycle as an instant rollback.
//
// It reuses the real Ch 22 operational types — BackfillScheduler, DriftAdapter,
// DocumentReingestService — rather than re-implementing them, so this is a wiring
// sketch over production code, not a toy. Everything is deterministic and needs no
// key; the process exits 0 when the migration completes (and the eval gate passes).
//
// Run: dotnet run --project tools/SmartDocs.Reindex

using Microsoft.Extensions.Logging.Abstractions;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Ingestion.Events;
using SmartDocs.Operations;
using SmartDocs.Reindex;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.VectorStores;

const string OldModel = "stub-embed-v1";
const string NewModel = "stub-embed-v2";

Console.WriteLine("=== SmartDocs reindex — zero-downtime embedding-model migration (offline sketch) ===");
Console.WriteLine();

// --- Two embedding "models": old (live) and new (target). ----------------------
var oldEmbedder = new EmbeddingService(
    new StubEmbeddingGenerator(OldModel, salt: 0x1111_1111),
    OldModel,
    StubEmbeddingGenerator.Dimensions,
    NullLogger<EmbeddingService>.Instance);

var newEmbedder = new EmbeddingService(
    new StubEmbeddingGenerator(NewModel, salt: 0x2222_2222),
    NewModel,
    StubEmbeddingGenerator.Dimensions,
    NullLogger<EmbeddingService>.Instance);

// --- The live index, seeded with a corpus on the OLD model. --------------------
var liveIndex = new InMemoryVectorStore("smartdocs-live");
await liveIndex.EnsureCollectionExistsAsync();

var corpus = BuildCorpus();
foreach (var chunk in corpus)
{
    await liveIndex.UpsertAsync([await oldEmbedder.EmbedAsync(chunk)]);
}
Console.WriteLine($"[seed]    live index holds {corpus.Count} chunks on '{OldModel}'.");

// === Stage 1 — shadow index ====================================================
var shadowIndex = new InMemoryVectorStore("smartdocs-shadow");
await shadowIndex.EnsureCollectionExistsAsync();
Console.WriteLine("[stage 1] shadow index created beside the live index.");

// === Stage 2 — dual-write ======================================================
// A new document arrives mid-migration. It must land in BOTH indexes so the
// shadow never falls behind the live one. The Ch 22 DocumentReingestService runs
// the delete-old-then-insert sequence; we point one at each index/model.
var liveReingest = new DocumentReingestService(RechunkAsync, oldEmbedder, liveIndex);
var shadowReingest = new DocumentReingestService(RechunkAsync, newEmbedder, shadowIndex);

await liveReingest.ReingestAsync("doc-new");
await shadowReingest.ReingestAsync("doc-new");
Console.WriteLine("[stage 2] dual-write: 'doc-new' indexed into live (old model) AND shadow (new model).");

// === Stage 3 — backfill the existing corpus into the shadow ====================
// Re-embed every old-model chunk with the new model and stage it into the shadow.
// BackfillScheduler in ParallelShadowThenCutover mode embeds the whole cohort and
// upserts it in one atomic write — exactly the shadow-build primitive.
var backfill = new BackfillScheduler(newEmbedder, shadowIndex, backoff: _ => TimeSpan.Zero);
var progress = await backfill.RunAsync(
    EnumerateOldModelChunks(corpus),
    new BackfillOptions(OldModel, BackfillSchedule.ParallelShadowThenCutover, BatchSize: 16));
Console.WriteLine($"[stage 3] backfill complete: {progress.Processed}/{progress.Total} chunks re-embedded into the shadow.");

// --- DriftAdapter — the query-time bridge while both spaces coexist. -----------
// Train a rotation old->new on a handful of paired vectors so a query embedded by
// the new model can still probe any not-yet-migrated old vectors. This is the
// "adapt instead of re-embed" lever from Ch 22; here it proves the spaces are
// reconcilable before we trust the cutover.
var drift = new DriftAdapter();
var sample = corpus.Take(8).ToList();
var oldVecs = new ReadOnlyMemory<float>[sample.Count];
var newVecs = new ReadOnlyMemory<float>[sample.Count];
for (int i = 0; i < sample.Count; i++)
{
    oldVecs[i] = (await oldEmbedder.EmbedAsync(sample[i])).Vector;
    newVecs[i] = (await newEmbedder.EmbedAsync(sample[i])).Vector;
}
drift.Train(oldVecs, newVecs);
Console.WriteLine("[stage 3] drift adapter trained (old->new rotation) as the transitional query bridge.");

// === Stage 4 — eval-gate the shadow ============================================
// Retrieval-quality gate: run the gold queries against BOTH indexes and require
// the shadow's hit-rate to be at least the baseline (the live index's). A shadow
// that regresses retrieval is NOT promoted — the migration aborts with exit 1.
var queries = BuildGoldQueries();
double liveHitRate = await HitRateAsync(liveIndex, oldEmbedder, queries);
double shadowHitRate = await HitRateAsync(shadowIndex, newEmbedder, queries);
Console.WriteLine($"[stage 4] eval gate: live hit-rate={liveHitRate:P0}, shadow hit-rate={shadowHitRate:P0}.");

const double Tolerance = 0.0001;
if (shadowHitRate + Tolerance < liveHitRate)
{
    Console.Error.WriteLine("[stage 4] GATE BLOCK — shadow retrieval regressed below baseline; aborting cutover.");
    return 1;
}
Console.WriteLine("[stage 4] GATE PASS — shadow meets the baseline; cleared for cutover.");

// === Stage 5 — atomic cutover ==================================================
// Reads flip to the shadow in one step. In production this is a config/alias swap;
// here we simply select the shadow retriever as the live one.
var liveRetriever = new DenseRetriever(newEmbedder, shadowIndex);
Console.WriteLine("[stage 5] atomic cutover — reads now served from the shadow (new model).");

// === Stage 6 — keep the old index one cycle as rollback ========================
// The old index is retained, not dropped, so a regression caught in production is
// one swap away from rollback. A later cycle reclaims it.
Console.WriteLine("[stage 6] old index retained for one cycle as the rollback target (not dropped).");

// --- Smoke-check the post-cutover read path. -----------------------------------
var sampleHits = await liveRetriever.RetrieveAsync(queries[0].Query, topK: 3);
Console.WriteLine($"[verify]  post-cutover query '{queries[0].Query}' returned {sampleHits.Count} hits from the new index.");

Console.WriteLine();
Console.WriteLine("=== migration complete — exit 0 ===");
return 0;

// --- Helpers -------------------------------------------------------------------

static async IAsyncEnumerable<EmbeddedChunk> EnumerateOldModelChunks(IReadOnlyList<DocumentChunk> chunks)
{
    // Source of "what is still on the old model" for the backfill. In production
    // this is a store query (WHERE EmbeddingModel == oldModel); here it is the
    // seeded corpus, each tagged with the old model so the scheduler picks it up.
    foreach (var chunk in chunks)
    {
        await Task.Yield();
        yield return new EmbeddedChunk(chunk, ReadOnlyMemory<float>.Empty, OldModel);
    }
}

static async Task<double> HitRateAsync(
    IVectorStore index,
    IEmbeddingService embedder,
    IReadOnlyList<GoldQuery> queries)
{
    var retriever = new DenseRetriever(embedder, index);
    int hits = 0;
    foreach (var q in queries)
    {
        var results = await retriever.RetrieveAsync(q.Query, topK: 3);
        var ids = results.Select(r => r.Chunk.DocumentId).ToHashSet(StringComparer.Ordinal);
        if (ids.Contains(q.ExpectedDocumentId))
        {
            hits++;
        }
    }
    return queries.Count == 0 ? 0 : (double)hits / queries.Count;
}

static Task<IReadOnlyList<DocumentChunk>> RechunkAsync(string documentId, CancellationToken ct)
{
    // The "re-chunk this document" hook the reingest service calls. A fixed
    // single-chunk document stands in for the load+chunk path.
    var meta = Metadata(documentId);
    IReadOnlyList<DocumentChunk> chunks =
    [
        new($"{documentId}#0", documentId, 0,
            "Updated policy: remote work is permitted four days per week with manager approval.",
            0, 80, meta),
    ];
    return Task.FromResult(chunks);
}

static List<DocumentChunk> BuildCorpus()
{
    string[] texts =
    [
        "Employees accrue twenty paid vacation days per fiscal year.",
        "Sick leave is unlimited for staff in good standing.",
        "Remote work is allowed up to three days per week with approval.",
        "Performance reviews occur annually each March.",
        "Parental leave provides eighteen weeks of fully paid time off.",
        "Expense reports must be filed within thirty days of travel.",
        "The security team rotates on-call duty every two weeks.",
        "All laptops are encrypted and enrolled in mobile device management.",
        "Health benefits enrolment opens each November for the following year.",
        "Stock options vest over four years with a one-year cliff.",
    ];

    var corpus = new List<DocumentChunk>(texts.Length);
    for (int i = 0; i < texts.Length; i++)
    {
        var docId = $"doc-{i:00}";
        corpus.Add(new DocumentChunk($"{docId}#0", docId, 0, texts[i], 0, texts[i].Length, Metadata(docId)));
    }
    return corpus;
}

static IReadOnlyList<GoldQuery> BuildGoldQueries() =>
[
    new("How many vacation days do employees get?", "doc-00"),
    new("What is the remote work policy?", "doc-02"),
    new("When do performance reviews happen?", "doc-03"),
    new("How long is parental leave?", "doc-04"),
    new("How do stock options vest?", "doc-09"),
];

static DocumentMetadata Metadata(string docId) =>
    new(docId, "hr-policies", "HR", "Global", "Internal", "Policy",
        2026, "Author", new DateOnly(2026, 1, 1), "HR Policy");

namespace SmartDocs.Reindex
{
    /// <summary>A gold retrieval query: the question and the document it should surface.</summary>
    internal sealed record GoldQuery(string Query, string ExpectedDocumentId);
}
