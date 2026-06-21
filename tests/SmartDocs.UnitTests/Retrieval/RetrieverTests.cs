using Microsoft.Extensions.Logging.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.VectorStores;

namespace SmartDocs.UnitTests.Retrieval;

public sealed class RetrieverTests
{
    private static DocumentChunk Chunk(string id, string text)
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk(id + "#0", id, 0, text, 0, text.Length, meta);
    }

    private static EmbeddedChunk Embed(string id, string text, float[] vec)
        => new(Chunk(id, text), vec, "stub");

    // Wrap a raw stub generator as the IEmbeddingService DenseRetriever now
    // depends on. The default EmbeddingPrompt.None passes text through unchanged,
    // so the query vector is identical to calling the generator directly.
    private static EmbeddingService Service(StubEmbeddingGenerator stub)
        => new(stub, "stub-model", 1, NullLogger<EmbeddingService>.Instance);

    [Fact]
    public async Task Dense_retriever_returns_top_k_from_in_memory_store()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(new[]
        {
            Embed("a", "vacation", [1f, 0f]),
            Embed("b", "remote",   [0f, 1f]),
            Embed("c", "vacation policy", [0.95f, 0.05f]),
        });
        var stub = new StubEmbeddingGenerator(_ => [1f, 0f]);
        var dense = new DenseRetriever(Service(stub), store);

        var hits = await dense.RetrieveAsync("vacation", topK: 2);

        Assert.Equal(2, hits.Count);
        Assert.Equal("a", hits[0].Chunk.DocumentId);
    }

    [Fact]
    public async Task Sparse_BM25_ranks_keyword_overlap()
    {
        var sparse = new SparseRetriever();
        sparse.Index(new[]
        {
            Chunk("a", "Employees receive 20 paid vacation days per fiscal year."),
            Chunk("b", "Sick leave is unlimited for employees in good standing."),
            Chunk("c", "Remote work is allowed up to 3 days per week."),
        });

        var hits = await sparse.RetrieveAsync("vacation days", topK: 2);

        Assert.NotEmpty(hits);
        Assert.Equal("a", hits[0].Chunk.DocumentId);
    }

    [Fact]
    public void RrfMerger_promotes_chunks_appearing_in_both_lists()
    {
        var common = Chunk("c", "shared");
        var aOnly = Chunk("a", "dense-only");
        var bOnly = Chunk("b", "sparse-only");

        var dense = new[]
        {
            new RetrievalResult(common, 0.9),
            new RetrievalResult(aOnly,  0.8),
        };
        var sparse = new[]
        {
            new RetrievalResult(common, 5.0),
            new RetrievalResult(bOnly,  4.0),
        };

        var merged = new RrfMerger(k: 60).Merge(dense, sparse, topK: 3);

        Assert.Equal(3, merged.Count);
        // `common` appears in both lists with rank 1 each -> highest fused score.
        Assert.Equal("c", merged[0].Chunk.DocumentId);
    }

    [Fact]
    public async Task Hybrid_retriever_runs_both_legs_and_merges()
    {
        var store = new InMemoryVectorStore();
        await store.UpsertAsync(new[]
        {
            Embed("vacation-doc", "Employees receive 20 paid vacation days.", [1f, 0f]),
            Embed("remote-doc",   "Remote work allowed 3 days per week.",     [0f, 1f]),
        });
        var stubEmb = new StubEmbeddingGenerator(_ => [1f, 0f]);
        var dense = new DenseRetriever(Service(stubEmb), store);

        var sparse = new SparseRetriever();
        sparse.Index(new[]
        {
            Chunk("vacation-doc", "Employees receive 20 paid vacation days."),
            Chunk("remote-doc",   "Remote work allowed 3 days per week."),
        });

        var hybrid = new HybridRetriever(dense, sparse);

        var hits = await hybrid.RetrieveAsync("vacation days", topK: 2);

        Assert.NotEmpty(hits);
        Assert.Equal("vacation-doc", hits[0].Chunk.DocumentId);
    }
}
