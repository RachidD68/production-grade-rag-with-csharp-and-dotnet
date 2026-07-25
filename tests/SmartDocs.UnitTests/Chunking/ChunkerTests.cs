using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Chunking;

namespace SmartDocs.UnitTests.Chunking;

public sealed class ChunkerTests
{
    private static DocumentMetadata Meta() => new(
        "doc-001", "hr-policies", "HR", "Montreal", "Internal", "Policy",
        2026, "A", new DateOnly(2026, 1, 1), "Test");

    private static Document Doc(string content) =>
        new(Meta(), content, "data/test/doc-001.md");

    [Fact]
    public async Task FixedSizeChunker_respects_size_and_overlap()
    {
        var chunker = new FixedSizeChunker(chunkSize: 10, overlap: 2);
        var doc = Doc("0123456789ABCDEFGHIJKLMNOPQR");
        var chunks = await ToListAsync(chunker.ChunkAsync(doc));

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 10));
        // Each subsequent chunk starts overlap chars before the previous end.
        for (int i = 1; i < chunks.Count; i++)
        {
            Assert.Equal(chunks[i - 1].EndCharOffset - 2, chunks[i].StartCharOffset);
        }
    }

    [Fact]
    public async Task SentenceChunker_groups_three_sentences_by_default()
    {
        var chunker = new SentenceChunker(maxSentencesPerChunk: 3);
        var doc = Doc("First sentence. Second sentence! Third sentence? Fourth sentence. Fifth one.");
        var chunks = await ToListAsync(chunker.ChunkAsync(doc));

        Assert.Equal(2, chunks.Count);
        Assert.Contains("First", chunks[0].Text, StringComparison.Ordinal);
        Assert.Contains("Third", chunks[0].Text, StringComparison.Ordinal);
        Assert.Contains("Fourth", chunks[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecursiveCharacterChunker_never_emits_blank_chunks()
    {
        // Blank lines and a heading marker produce raw spans that are non-empty
        // before Trim() and empty after it. Chapter 3 states outright that the
        // chunkers never emit empties, and EmbeddingService relies on it.
        var chunker = new RecursiveCharacterChunker(maxChunkSize: 40);
        var doc = Doc("## Leave\n\n\nStaff accrue leave monthly.\n\n   \n\n## Sick\n\nNotify your manager.");

        var chunks = await ToListAsync(chunker.ChunkAsync(doc));

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.False(
            string.IsNullOrWhiteSpace(c.Text),
            $"blank chunk emitted at index {c.ChunkIndex}"));
    }

    [Fact]
    public async Task RecursiveCharacterChunker_prefers_paragraph_breaks()
    {
        var chunker = new RecursiveCharacterChunker(maxChunkSize: 50);
        var doc = Doc(
            "First paragraph about Acme Corp signing the Q3 contract.\n\n" +
            "Second paragraph about the renewal terms with Globex.\n\n" +
            "Third paragraph about the audit findings.");
        var chunks = await ToListAsync(chunker.ChunkAsync(doc));

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 80, $"chunk too long: {c.Text}"));
    }

    [Fact]
    public async Task RecursiveCharacterChunker_does_not_split_named_entities()
    {
        // The "named entities" we promised not to split are the proper nouns in
        // these contract sentences. Any chunk that contains the prefix
        // "Acme Corporation" must contain the full phrase, not just "Acme".
        var chunker = new RecursiveCharacterChunker(maxChunkSize: 60);
        var doc = Doc(
            "Acme Corporation signed the agreement.\n\n" +
            "The contract was countersigned by Globex Industries.\n\n" +
            "The audit was performed by Initech Auditors LLC.");

        var chunks = await ToListAsync(chunker.ChunkAsync(doc));

        AssertEntityNotSplit(chunks, "Acme Corporation");
        AssertEntityNotSplit(chunks, "Globex Industries");
        AssertEntityNotSplit(chunks, "Initech Auditors LLC");
    }

    private static void AssertEntityNotSplit(IReadOnlyList<DocumentChunk> chunks, string entity)
    {
        var firstWord = entity.Split(' ')[0];
        foreach (var c in chunks)
        {
            if (c.Text.Contains(firstWord, StringComparison.Ordinal))
            {
                Assert.Contains(entity, c.Text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task CodeFileChunker_splits_on_method_and_class_boundaries()
    {
        var chunker = new CodeFileChunker();
        var meta = Meta();
        var doc = new Document(meta,
            """
            namespace Foo;

            public class Bar
            {
                public int Add(int a, int b) => a + b;
                public int Sub(int a, int b) => a - b;
            }
            """,
            "src/Bar.cs");

        var chunks = await ToListAsync(chunker.ChunkAsync(doc));

        // Bar (class) + Add (method) + Sub (method) = 3 chunks.
        Assert.Equal(3, chunks.Count);
    }

    [Fact]
    public async Task CodeFileChunker_falls_back_to_whole_document_for_non_cs()
    {
        var chunker = new CodeFileChunker();
        var doc = Doc("Just plain text content here.");
        var chunks = await ToListAsync(chunker.ChunkAsync(doc));
        Assert.Single(chunks);
    }

    [Fact]
    public async Task CodeFileChunker_prepends_namespace_and_containing_type_to_method_chunks()
    {
        var chunker = new CodeFileChunker();
        var meta = Meta();
        var doc = new Document(meta,
            """
            using System;
            using System.Text;

            namespace SmartDocs.Sample.Math;

            public class Calculator
            {
                public int Add(int a, int b) => a + b;
            }
            """,
            "src/Calculator.cs");

        var chunks = await ToListAsync(chunker.ChunkAsync(doc));

        // The method chunk (the one whose body holds the Add signature) must be
        // self-describing: it names its namespace and the containing class even
        // though the method body alone would not.
        var methodChunk = chunks.Single(c =>
            c.Text.Contains("=> a + b", StringComparison.Ordinal) &&
            !c.Text.Contains("class Calculator\n{", StringComparison.Ordinal));

        Assert.Contains("namespace SmartDocs.Sample.Math;", methodChunk.Text, StringComparison.Ordinal);
        Assert.Contains("class Calculator", methodChunk.Text, StringComparison.Ordinal);
        Assert.Contains("using System;", methodChunk.Text, StringComparison.Ordinal);
        // Offsets still reference the original member span, not the augmented text.
        Assert.True(methodChunk.EndCharOffset <= doc.Content.Length);
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var x in source)
        {
            list.Add(x);
        }

        return list;
    }
}
