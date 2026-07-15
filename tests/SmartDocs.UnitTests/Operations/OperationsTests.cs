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
    public void DriftAdapter_recovers_known_rotation()
    {
        // Build a known 3-D rotation R (about the z-axis by 35°). Train the adapter
        // on pairs (v, R·v) for a spanning set of v, then a held-out query must map
        // through (the now-learned) R to within tolerance. This is the real
        // Orthogonal-Procrustes recovery the chapter claims, not an identity stub.
        const double Theta = 35.0 * Math.PI / 180.0;
        var c = (float)Math.Cos(Theta);
        var s = (float)Math.Sin(Theta);
        float[] Rotate(float[] v) =>
            [c * v[0] - s * v[1], s * v[0] + c * v[1], v[2]];

        float[][] basis =
        [
            [1f, 0f, 0f],
            [0f, 1f, 0f],
            [0f, 0f, 1f],
            [0.6f, -0.3f, 0.74f],
        ];
        var oldVectors = basis.Select(v => new ReadOnlyMemory<float>(v)).ToArray();
        var newVectors = basis.Select(v => new ReadOnlyMemory<float>(Rotate(v))).ToArray();

        var adapter = new DriftAdapter();
        adapter.Train(oldVectors, newVectors);

        // A held-out unit query (not in the training basis).
        var query = new float[] { 0.2f, 0.5f, 0.84f };
        var qNorm = MathF.Sqrt(query.Sum(x => x * x));
        for (int i = 0; i < query.Length; i++)
        {
            query[i] /= qNorm;
        }

        var mapped = adapter.Apply(query);

        var expected = Rotate(query);
        var eNorm = MathF.Sqrt(expected.Sum(x => x * x));
        for (int i = 0; i < expected.Length; i++)
        {
            expected[i] /= eNorm;
        }

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(expected[i], mapped[i], precision: 3);
        }
    }

    [Fact]
    public void DriftAdapter_trained_on_identical_pairs_is_near_identity()
    {
        // old == new ⇒ the optimal rotation is the identity, so Apply returns the
        // (unit-normalized) input direction unchanged.
        var adapter = new DriftAdapter();
        float[][] basis = [[1f, 0f, 0f], [0f, 1f, 0f], [0f, 0f, 1f]];
        var vectors = basis.Select(v => new ReadOnlyMemory<float>(v)).ToArray();
        adapter.Train(vectors, vectors);

        var input = new float[] { 0.5f, 0.5f, MathF.Sqrt(0.5f) };
        var n = MathF.Sqrt(input.Sum(x => x * x));
        for (int i = 0; i < input.Length; i++)
        {
            input[i] /= n;
        }

        var mapped = adapter.Apply(input);
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(input[i], mapped[i], precision: 3);
        }
    }

    [Fact]
    public void DriftAdapter_rejects_dimension_mismatch()
    {
        var adapter = new DriftAdapter();
        Assert.Throws<ArgumentException>(() => adapter.Train(
            [new ReadOnlyMemory<float>([1f, 0f, 0f])],
            [new ReadOnlyMemory<float>([1f, 0f])])); // new is 2-D, old is 3-D
    }

    [Fact]
    public async Task BackfillScheduler_reembeds_only_old_model_chunks()
    {
        var store = new InMemoryVectorStore();
        var embedder = new StubEmbeddingService("v2");

        // Mixed cohort: two chunks on the old model, one already on the new model.
        var candidates = new[]
        {
            new EmbeddedChunk(Chunk("a", "alpha"), new ReadOnlyMemory<float>([1f, 0f]), "v1"),
            new EmbeddedChunk(Chunk("b", "bravo"), new ReadOnlyMemory<float>([0f, 1f]), "v1"),
            new EmbeddedChunk(Chunk("c", "charlie"), new ReadOnlyMemory<float>([1f, 1f]), "v2"),
        };

        var scheduler = new BackfillScheduler(embedder, store, backoff: _ => TimeSpan.Zero);
        var progress = await scheduler.RunAsync(
            ToAsync(candidates),
            new BackfillOptions(OldModel: "v1", BatchSize: 1));

        Assert.Equal(2, progress.Total);          // only the two v1 chunks are candidates
        Assert.Equal(2, progress.Processed);
        Assert.True(progress.Done);
        Assert.Equal(["a#0", "b#0"], embedder.Embedded.OrderBy(x => x)); // c (v2) never touched
    }

    [Fact]
    public async Task BackfillScheduler_resume_skips_completed_chunks()
    {
        var store = new InMemoryVectorStore();
        var embedder = new StubEmbeddingService("v2");
        var candidates = new[]
        {
            new EmbeddedChunk(Chunk("a", "alpha"), new ReadOnlyMemory<float>([1f, 0f]), "v1"),
            new EmbeddedChunk(Chunk("b", "bravo"), new ReadOnlyMemory<float>([0f, 1f]), "v1"),
        };

        // Resume with a#0 already done.
        var resume = new BackfillProgress(["a#0"]);
        var scheduler = new BackfillScheduler(embedder, store, backoff: _ => TimeSpan.Zero);
        var progress = await scheduler.RunAsync(
            ToAsync(candidates),
            new BackfillOptions(OldModel: "v1", BatchSize: 10),
            resume);

        Assert.Equal(2, progress.Total);
        Assert.True(progress.Done);
        Assert.Equal(["b#0"], embedder.Embedded); // a#0 skipped; only b#0 re-embedded
    }

    private static async IAsyncEnumerable<EmbeddedChunk> ToAsync(IEnumerable<EmbeddedChunk> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    /// <summary>A deterministic, offline embedding service that records what it embedded.</summary>
    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public StubEmbeddingService(string model) => EmbeddingModel = model;

        public List<string> Embedded { get; } = [];
        public string EmbeddingModel { get; }
        public int Dimensions => 2;

        public Task<EmbeddedChunk> EmbedAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
        {
            Embedded.Add(chunk.ChunkId);
            return Task.FromResult(new EmbeddedChunk(chunk, new ReadOnlyMemory<float>([1f, 1f]), EmbeddingModel));
        }

        public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReadOnlyMemory<float>([1f, 1f]));

        public async IAsyncEnumerable<EmbeddedChunk> EmbedAsync(
            IAsyncEnumerable<DocumentChunk> chunks,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in chunks.WithCancellation(cancellationToken))
            {
                yield return await EmbedAsync(chunk, cancellationToken);
            }
        }
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
