// Chapter 24 — Trust by Design.
//
// End-to-end "trust by design" walkthrough wired to the real production types in
// src/SmartDocs.Operations/Compliance/*. The flow mirrors the chapter:
//
//   1. Ingest a small corpus that includes one user's content, signing each
//      chunk's provenance with the Ch 23 HMAC signer.
//   2. Run a couple of grounded queries and log them to the EU AI Act audit log.
//   3. Honor a GDPR Article 17 erasure request via the Ch 22 deletion pipeline.
//   4. Show the signed ErasureReceipt (chapter JSON shape) and verify it.
//   5. Trace a historical answer back to its sources with CitationAuditor,
//      proving each cited chunk's provenance signature.
//   6. Render and sign the live model card.
//
// Runs fully offline and exits 0. There is no chat client here: retrieval is real
// (InMemoryVectorStore + a deterministic toy embedder) but the answers are canned
// strings passed in alongside each query, so the compliance flow can be exercised
// without a model call.
//
// Run:
//   dotnet run --project samples/Ch24_TrustByDesign

using System.Text;
using SmartDocs.Core.Documents;
using SmartDocs.Generation.Citations;
using SmartDocs.Mcp;
using SmartDocs.Operations;
using SmartDocs.Operations.Compliance;
using SmartDocs.Retrieval.VectorStores;
using SmartDocs.Security;

Console.WriteLine("=== Ch24: Trust by Design ===");
Console.WriteLine();

// --- Shared keys / signers (would live in Key Vault in production) ---

var provenanceKey = Encoding.UTF8.GetBytes("ch24-provenance-key");
var receiptKey = Encoding.UTF8.GetBytes("ch24-receipt-key");
var cardKey = Encoding.UTF8.GetBytes("ch24-model-card-key");

var signer = new HmacProvenanceSigner(provenanceKey);

// --- 1. Ingest a small corpus, including one user's (alice's) content ---

DocumentMetadata Meta(string id, string title, string author) => new(
    Id: id,
    Silo: "hr-policies",
    Department: "HR",
    Office: "Montreal",
    ConfidentialityLevel: "Internal",
    DocumentType: "Policy",
    FiscalYear: 2026,
    Author: author,
    LastModified: new DateOnly(2026, 5, 10),
    Title: title);

DocumentChunk SignedChunk(string docId, int index, string text, DocumentMetadata meta)
{
    var chunk = new DocumentChunk(
        ChunkId: $"{docId}#{index}",
        DocumentId: docId,
        ChunkIndex: index,
        Text: text,
        StartCharOffset: 0,
        EndCharOffset: text.Length,
        Metadata: meta);
    return chunk with { Provenance = signer.Sign(chunk) };
}

var hrMeta = Meta("hr-001", "Vacation Policy 2026", "HR Team");
var aliceMeta = Meta("usr-alice", "Alice — Accommodation Request", "alice@contoso.com");

var corpus = new List<DocumentChunk>
{
    SignedChunk("hr-001", 0, "Employees are entitled to 25 vacation days per year. Unused days carry over up to 5.", hrMeta),
    SignedChunk("hr-001", 1, "Vacation requests must be submitted at least two weeks in advance.", hrMeta),
    SignedChunk("usr-alice", 0, "Alice requested a standing-desk accommodation effective March 2026.", aliceMeta),
};

// Embed with a trivial deterministic embedder so the store is searchable offline.
ReadOnlyMemory<float> Embed(string text)
{
    // 8-dim bag-of-chars hash; good enough to make cosine search deterministic.
    var v = new float[8];
    foreach (var ch in text)
    {
        v[ch % 8] += 1f;
    }
    var norm = MathF.Sqrt(v.Sum(x => x * x));
    if (norm > 0)
    {
        for (var i = 0; i < v.Length; i++)
        {
            v[i] /= norm;
        }
    }
    return v;
}

var store = new InMemoryVectorStore("hr-policies");
await store.UpsertAsync(corpus.Select(c => new EmbeddedChunk(c, Embed(c.Text), "stub-embed")));

var lookup = new InMemoryChunkLookup(corpus);
var docStore = new InMemoryDocumentMetadataStore();
docStore.Put(hrMeta);
docStore.Put(aliceMeta);

Console.WriteLine($"Ingested {corpus.Count} signed chunks across 2 documents (incl. one user's content).");
Console.WriteLine();

// --- 2. Run a grounded query and log it to the audit store ---

var auditStore = new InMemoryAuditRecordStore();

async Task RunQueryAsync(string queryId, string query, string answer, string citedChunkId, int sourceIndex)
{
    var hits = await store.SearchAsync(Embed(query), topK: 3);
    var citedChunk = corpus.First(c => c.ChunkId == citedChunkId);
    var citation = new Citation(
        ClaimText: answer,
        SourceIndex: sourceIndex,
        ChunkId: citedChunk.ChunkId,
        DocumentId: citedChunk.DocumentId,
        Title: citedChunk.Metadata.Title,
        Confidence: "SUPPORTED");

    var entry = new AuditEntry(
        TimestampUtc: DateTimeOffset.UtcNow,
        Query: query,
        RetrievedChunkIds: [.. hits.Select(h => h.Chunk.ChunkId)],
        Response: answer,
        Citations: [citation],
        FaithfulnessScore: 0.95,
        UserId: "user-42");
    auditStore.Put(queryId, entry);
    Console.WriteLine($"  [{queryId}] \"{query}\" -> {hits.Count} hits, logged with 1 citation.");
}

Console.WriteLine("--- Queries (logged to the EU AI Act audit store) ---");
await RunQueryAsync("q-1001", "How many vacation days do I get?",
    "You are entitled to 25 vacation days per year. [Source 1]", "hr-001#0", sourceIndex: 1);
await RunQueryAsync("q-1002", "How far in advance must I request vacation?",
    "Vacation requests must be submitted at least two weeks in advance. [Source 1]", "hr-001#1", sourceIndex: 1);
Console.WriteLine();

// --- 3. Honor a GDPR Article 17 erasure request for alice ---

Console.WriteLine("--- GDPR erasure (Ch 22 pipeline) ---");
var receivedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
var graphDeleted = new List<string>();
var pipeline = new GdprDeletionPipeline(
    store,
    id => { graphDeleted.Add(id); return Task.CompletedTask; },
    _ => { });

var deletion = await pipeline.DeleteAsync(
    subjectId: "usr-alice",
    chunkIds: ["usr-alice#0"],
    graphEntityIds: ["entity-alice"],
    requestedBy: "dpo@contoso.com");
Console.WriteLine($"  Erased {deletion.ChunkIds.Count} chunk(s) + {deletion.GraphEntityIds.Count} graph entity(ies) for {deletion.SubjectId}.");
Console.WriteLine();

// --- 4. Signed erasure receipt (chapter JSON shape) ---

Console.WriteLine("--- Signed erasure receipt ---");
var receiptGen = new ErasureReceiptGenerator(ErasureReceiptGenerator.HmacSigner(receiptKey));
var receipt = receiptGen.Build(
    requestId: "req-7788",
    receivedAt: receivedAt,
    deletion: deletion,
    redactedAuditRecords: 1);
Console.WriteLine(ErasureReceiptGenerator.ToJson(receipt));
Console.WriteLine($"  Receipt signature verifies: {receiptGen.Verify(receipt)}");
Console.WriteLine();

// --- 5. Trace a historical answer back to its sources ---

Console.WriteLine("--- Citation auditor (trace q-1001) ---");
var auditor = new CitationAuditor(auditStore, lookup, docStore, signer);
var trace = await auditor.TraceAsync("q-1001");
Console.WriteLine($"  Query: {trace.QueryId}");
Console.WriteLine($"  Answer: {trace.Answer}");
foreach (var step in trace.Chain)
{
    Console.WriteLine(
        $"    [Source {step.CitationN}] {step.ChunkId} ({step.SourceUri}) " +
        $"modified {step.ModifiedAt:yyyy-MM-dd} — signature valid: {step.SignatureValid}");
}
Console.WriteLine();

// --- 6. Render and sign the live model card ---

Console.WriteLine("--- Model card (live snapshot, signed) ---");
var modelRegistry = new StubModelRegistry();
var corpusInventory = new StubCorpusInventory();
var profile = new ModelCardProfile(
    SystemName: "SmartDocs RAG",
    Version: "2.1.0",
    IntendedUse: "Answer employee questions over internal HR, technical, and legal corpora with cited sources.",
    PerformanceMetrics:
    [
        new MetricEntry("faithfulness", 0.94),
        new MetricEntry("recall@5", 0.88),
    ],
    KnownFailureModes:
    [
        "May abstain when retrieval score is below threshold.",
        "Does not read images or tables embedded in source PDFs.",
    ],
    OperationalControls:
    [
        "Every answer carries source citations and a logged audit trace.",
        "PII detected at ingest is redacted before indexing.",
        "Cross-tenant retrieval is blocked post-retrieval by the tenant guard.",
    ]);

var cardGen = new ModelCardGenerator(
    modelRegistry,
    corpusInventory,
    new MarkdownModelCardRenderer(),
    ModelCardGenerator.HmacSigner(cardKey),
    profile);

var (markdown, cardSignature) = await cardGen.GenerateSignedAsync();
Console.WriteLine(markdown);
Console.WriteLine($"Detached signature (HMAC-SHA256): {cardSignature}");
Console.WriteLine();

Console.WriteLine("Done.");
return;

// --- Offline stub ports for the model card ---

sealed class StubModelRegistry : IModelRegistry
{
    public Task<IReadOnlyList<ModelEntry>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        // Offline stub: a fixed, DATED record — which is the whole point of a model
        // card (Chapter 24: pin the dated version, never the alias). The id is a
        // now-retired one on purpose; a real IModelRegistry reads name + version
        // from the live deployment (deploy/modules/openai.bicep now targets
        // gpt-5.6-terra on the platform-default version), and a card must keep
        // recording what WAS deployed after the model is retired.
        IReadOnlyList<ModelEntry> models =
        [
            new("chat", "gpt-4o", "2024-11-20"),
            new("embedding", "text-embedding-3-small", "1"),
            new("reranker", "bge-reranker-v2-m3", "1.0"),
        ];
        return Task.FromResult(models);
    }
}

sealed class StubCorpusInventory : ICorpusInventory
{
    public Task<IReadOnlyList<DataSourceEntry>> GetDataSourcesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DataSourceEntry> sources =
        [
            new("hr-policies", 42, "Internal"),
            new("technical-docs", 118, "Internal"),
            new("legal-contracts", 27, "Confidential"),
        ];
        return Task.FromResult(sources);
    }
}
