using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Operations;
using SmartDocs.Retrieval.VectorStores;

namespace SmartDocs.UnitTests.Operations;

public sealed class OperationsTests
{
    private static DocumentChunk Chunk(string id, string text)
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk(id + "#0", id, 0, text, 0, text.Length, meta);
    }

    [Fact]
    public void MinHashDeduplicator_collapses_near_duplicates()
    {
        var dedup = new MinHashDeduplicator(permutations: 128, threshold: 0.7, seed: 42);
        var input = new[]
        {
            Chunk("a", "Employees receive 20 paid vacation days per fiscal year accrued monthly."),
            Chunk("b", "Employees receive 20 paid vacation days per fiscal year accrued monthly!"),
            Chunk("c", "Sick leave is unlimited for employees in good standing."),
        };
        var unique = dedup.Deduplicate(input);

        Assert.Equal(2, unique.Count);
    }

    [Fact]
    public void DriftAdapter_applies_identity_after_training()
    {
        var adapter = new DriftAdapter();
        adapter.Train(
            [new ReadOnlyMemory<float>([1f, 0f, 0f]), new ReadOnlyMemory<float>([0f, 1f, 0f])],
            [new ReadOnlyMemory<float>([1f, 0f, 0f]), new ReadOnlyMemory<float>([0f, 1f, 0f])]);

        var mapped = adapter.Apply(new ReadOnlyMemory<float>([0.5f, 0.5f, 0f]));
        Assert.Equal(0.5f, mapped[0], precision: 4);
        Assert.Equal(0.5f, mapped[1], precision: 4);
        Assert.Equal(0f, mapped[2], precision: 4);
    }

    [Fact]
    public async Task GdprDeletionPipeline_deletes_from_vector_and_graph_then_audits()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(new[]
        {
            new EmbeddedChunk(Chunk("a", "to be removed"), new ReadOnlyMemory<float>([1f, 0f]), "stub"),
        });

        var graphDeleted = new List<string>();
        DeletionAuditEntry? logged = null;
        var pipeline = new GdprDeletionPipeline(
            store,
            id => { graphDeleted.Add(id); return Task.CompletedTask; },
            entry => { logged = entry; });

        var entry = await pipeline.DeleteAsync(
            subjectId: "subject-1",
            chunkIds: ["a#0"],
            graphEntityIds: ["entity-acme"],
            requestedBy: "dpo@contoso.com");

        Assert.Equal("subject-1", entry.SubjectId);
        Assert.Single(graphDeleted);
        Assert.NotNull(logged);
        Assert.Equal("dpo@contoso.com", logged.RequestedBy);
        // Vector store no longer returns the deleted chunk.
        var hits = await store.SearchAsync(new ReadOnlyMemory<float>([1f, 0f]), topK: 5);
        Assert.Empty(hits);
    }
}
