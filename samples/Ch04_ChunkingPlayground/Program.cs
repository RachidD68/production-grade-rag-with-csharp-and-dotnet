// Chapter 4 — Chunking Playground.
//
// Applies four chunking strategies (FixedSize, Sentence, Recursive,
// CodeFile) to a sample HR-policy and a sample C# file, printing chunk
// count + average length + first 60 chars of each chunk side-by-side.
//
// Run:
//   dotnet run --project samples/Ch04_ChunkingPlayground

using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Chunking;

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

var strategies = new (string Name, IChunker Chunker, Document Doc)[]
{
    ("FixedSize(200, 50)",            new FixedSizeChunker(200, 50),   hrDoc),
    ("Sentence(3)",                    new SentenceChunker(3),          hrDoc),
    ("Recursive(200)",                 new RecursiveCharacterChunker(200), hrDoc),
    ("CodeFile (C#)",                  new CodeFileChunker(),           csDoc),
};

foreach (var (name, chunker, doc) in strategies)
{
    Console.WriteLine();
    Console.WriteLine($"=== {name} on {doc.SourcePath} ===");
    var chunks = new List<DocumentChunk>();
    await foreach (var c in chunker.ChunkAsync(doc))
    {
        chunks.Add(c);
    }

    var avgLen = chunks.Count == 0 ? 0 : chunks.Average(c => c.Text.Length);
    Console.WriteLine($"  count={chunks.Count}  avgLen={avgLen:F0}");
    foreach (var c in chunks.Take(5))
    {
        var preview = c.Text.Replace('\n', ' ');
        if (preview.Length > 60)
        {
            preview = preview[..60] + "...";
        }

        Console.WriteLine($"  [{c.ChunkIndex}] @{c.StartCharOffset}-{c.EndCharOffset}: {preview}");
    }
}
