// Chapter 4 — Chunking Playground.
//
// Loads three reference documents (an HR policy, a legal contract, and a C#
// source file) and runs every chunking strategy from the chapter side by side:
//
//   - the four classic strategies (FixedSize, Sentence, Recursive, Semantic)
//     on the prose documents,
//   - CodeFile on the .cs document,
//   - Contextual(Recursive) on the prose documents.
//
// Semantic and Contextual need a live model. The sample wires an Ollama
// IChatClient (llama3.2) and IEmbeddingGenerator (nomic-embed-text) using the
// book's defaults, probes the endpoint once, and degrades gracefully with a
// printed note when nothing is reachable — so it still builds and runs offline.
//
// Run:
//   dotnet run --project samples/Ch04_ChunkingPlayground

using Microsoft.Extensions.AI;
using OllamaSharp;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Chunking;

const int PreviewCount = 8; // print at least the first 8 chunks, so "chunk 7" exists.

var endpoint = new Uri(
    Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434");
const string ChatModel = "llama3.2";
const string EmbeddingModel = "nomic-embed-text";

// --- Reference documents -------------------------------------------------

var hrText =
    "## Annual Leave Policy\n\n" +
    "Employees at the Montreal office receive 20 paid vacation days per fiscal year, accrued monthly. " +
    "Unused days roll over up to a maximum of 10 into the next fiscal year. " +
    "Long-term leave beyond 30 days requires VP approval.\n\n" +
    "## Sick Leave\n\n" +
    "Sick leave is unlimited for employees in good standing; please notify your manager within 24 hours. " +
    "Long-term sick leave beyond 10 days requires a medical certificate.\n\n" +
    "## Remote Work\n\n" +
    "Remote work is allowed up to 3 days per week with prior manager approval.";

var hrMeta = new DocumentMetadata("hr-001", "hr-policies", "HR", "Montreal", "Internal", "Policy",
    2026, "Author", new DateOnly(2026, 1, 1), "Annual Leave Policy");
var hrDoc = new Document(hrMeta, hrText, "data/hr-policies/hr-001.md");

var contractText =
    "## 1. Scope of Services\n\n" +
    "The Vendor shall provide cloud hosting and support services to Contoso Ltd. for the term of " +
    "this agreement. Services include 99.9% uptime, 24/7 monitoring, and quarterly disaster-recovery " +
    "drills. Any change to the scope must be agreed in writing by both parties.\n\n" +
    "## 2. Payment Terms\n\n" +
    "Contoso shall pay the Vendor within 30 days of each invoice date. Late payments accrue interest at " +
    "1.5% per month. Fees are reviewed annually and may increase by no more than the prior year's CPI.\n\n" +
    "## 3. Termination\n\n" +
    "Either party may terminate this agreement with 60 days' written notice. Upon termination the Vendor " +
    "shall return or destroy all Contoso data within 15 days and certify destruction in writing.";

var contractMeta = new DocumentMetadata("contract-001", "legal-contracts", "Legal", "Paris", "Confidential",
    "Contract", 2026, "Author", new DateOnly(2026, 1, 1), "Master Services Agreement");
var contractDoc = new Document(contractMeta, contractText, "data/legal-contracts/contract-001.md");

var csText =
    "namespace SampleNs;\n\n" +
    "public class Calculator\n{\n" +
    "    public int Add(int a, int b) => a + b;\n" +
    "    public int Sub(int a, int b) => a - b;\n" +
    "    public int Mul(int a, int b) => a * b;\n" +
    "}\n";
var csMeta = new DocumentMetadata("code-001", "technical-docs", "Engineering", "Paris", "Internal", "Reference",
    2026, "Author", new DateOnly(2026, 1, 1), "Calculator.cs");
var csDoc = new Document(csMeta, csText, "src/Calculator.cs");

var proseDocs = new[] { hrDoc, contractDoc };

// --- Optional model-backed clients (Semantic + Contextual) ---------------

var online = await IsOllamaReachableAsync(endpoint).ConfigureAwait(false);
IChatClient? chat = null;
IEmbeddingGenerator<string, Embedding<float>>? embeddings = null;
if (online)
{
    chat = new OllamaApiClient(endpoint, ChatModel);
    embeddings = new OllamaApiClient(endpoint, EmbeddingModel);
    Console.WriteLine($"Ollama reachable at {endpoint} — Semantic and Contextual strategies enabled.");
}
else
{
    Console.WriteLine($"Ollama not reachable at {endpoint}.");
    Console.WriteLine("  Running the offline strategies only; Semantic and Contextual are skipped.");
    Console.WriteLine($"  Start Ollama and `ollama pull {ChatModel}` + `ollama pull {EmbeddingModel}` to enable them.");
}

// --- Strategy runs -------------------------------------------------------

// Classic char-based strategies run offline on both prose documents.
foreach (var doc in proseDocs)
{
    await RunAsync("FixedSize(200, 50)", new FixedSizeChunker(200, 50), doc).ConfigureAwait(false);
    await RunAsync("Sentence(2)", new SentenceChunker(2), doc).ConfigureAwait(false);
    await RunAsync("Recursive(200)", new RecursiveCharacterChunker(200), doc).ConfigureAwait(false);
}

// Code-aware strategy on the .cs document.
await RunAsync("CodeFile (C#)", new CodeFileChunker(), csDoc).ConfigureAwait(false);

// Model-backed strategies — Semantic and Contextual(Recursive) on the prose.
if (online && embeddings is not null && chat is not null)
{
    foreach (var doc in proseDocs)
    {
        await RunAsync("Semantic(0.6)", new SemanticChunker(embeddings, 0.6), doc).ConfigureAwait(false);
        await RunAsync(
            "Contextual(Recursive(200))",
            new ContextualChunker(new RecursiveCharacterChunker(200), chat),
            doc).ConfigureAwait(false);
    }
}
else
{
    Console.WriteLine();
    Console.WriteLine("=== Semantic / Contextual(Recursive) — SKIPPED (offline) ===");
    Console.WriteLine("  These strategies need a model. See the note above to enable them.");
}

// --- Helpers -------------------------------------------------------------

static async Task RunAsync(string name, IChunker chunker, Document doc)
{
    Console.WriteLine();
    Console.WriteLine($"=== {name} on {doc.SourcePath} ===");
    var chunks = new List<DocumentChunk>();
    await foreach (var c in chunker.ChunkAsync(doc).ConfigureAwait(false))
    {
        chunks.Add(c);
    }

    var avgLen = chunks.Count == 0 ? 0 : chunks.Average(c => c.Text.Length);
    Console.WriteLine($"  strategy={chunker.Strategy}  count={chunks.Count}  avgLen={avgLen:F0}");
    foreach (var c in chunks.Take(PreviewCount))
    {
        var preview = c.Text.Replace('\n', ' ');
        if (preview.Length > 60)
        {
            preview = preview[..60] + "...";
        }

        Console.WriteLine($"  [{c.ChunkIndex}] @{c.StartCharOffset}-{c.EndCharOffset}: {preview}");
    }
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
