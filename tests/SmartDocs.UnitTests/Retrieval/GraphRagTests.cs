using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Retrieval.Graph;
using SmartDocs.Retrieval.VectorStores;

namespace SmartDocs.UnitTests.Retrieval;

public sealed class GraphRagTests
{
    private static DocumentChunk Chunk(string id, string text)
    {
        var meta = new DocumentMetadata(id, "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        return new DocumentChunk(id + "#0", id, 0, text, 0, text.Length, meta);
    }

    [Fact]
    public async Task GraphRagPipeline_groups_connected_entities_into_communities()
    {
        var extractorChat = new StubChatClient(prompt =>
        {
            if (prompt.Contains("Acme contract", StringComparison.Ordinal))
            {
                return "{\"entities\":[{\"id\":\"acme\",\"type\":\"Client\",\"name\":\"Acme\"},{\"id\":\"msa-2026\",\"type\":\"Contract\",\"name\":\"MSA 2026\"}],\"relations\":[{\"from\":\"acme\",\"to\":\"msa-2026\",\"type\":\"SIGNED_BY\"}]}";
            }
            if (prompt.Contains("HR policy", StringComparison.Ordinal))
            {
                return "{\"entities\":[{\"id\":\"hr-dept\",\"type\":\"Department\",\"name\":\"HR\"},{\"id\":\"vac-policy\",\"type\":\"Document\",\"name\":\"Vacation Policy\"}],\"relations\":[{\"from\":\"hr-dept\",\"to\":\"vac-policy\",\"type\":\"AUTHORED\"}]}";
            }
            return "{\"entities\":[],\"relations\":[]}";
        });
        var extractor = new EntityExtractor(extractorChat);

        var summarizerChat = new StubChatClient(_ => "Summary of community");
        var pipeline = new GraphRagPipeline(extractor, summarizerChat);

        var summaries = await pipeline.IndexAsync(new[]
        {
            Chunk("c1", "Acme contract text"),
            Chunk("c2", "HR policy text"),
        });

        // Two disconnected components -> two communities.
        Assert.Equal(2, summaries.Count);
        Assert.Equal("Summary of community", summaries[0].Summary);
    }

    [Fact]
    public async Task LazyGraphRagRetriever_summarises_subgraph_only_at_query_time()
    {
        var extractorChat = new StubChatClient(_ =>
            "{\"entities\":[{\"id\":\"acme\",\"type\":\"Client\",\"name\":\"Acme\"}]}");
        var extractor = new EntityExtractor(extractorChat);

        var graph = new InMemoryGraph();
        graph.Entities.Add(new GraphEntity("acme", "Client", "Acme",
            new Dictionary<string, string> { ["industry"] = "manufacturing" }));
        graph.Entities.Add(new GraphEntity("msa", "Contract", "MSA",
            new Dictionary<string, string> { ["counterparty"] = "Acme" }));

        var summarizerChat = new StubChatClient(p =>
            p.Contains("Subgraph:", StringComparison.Ordinal) ? "Acme is a manufacturing client with an MSA contract." : "?");
        var lazy = new LazyGraphRagRetriever(extractor, graph, summarizerChat);

        var hits = await lazy.RetrieveAsync("Tell me about Acme", topK: 1);

        Assert.Single(hits);
        Assert.Contains("manufacturing", hits[0].Chunk.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LazyGraphRagRetriever_returns_cached_summary_on_second_identical_subgraph()
    {
        var extractorChat = new StubChatClient(_ =>
            "{\"entities\":[{\"id\":\"acme\",\"type\":\"Client\",\"name\":\"Acme\"}]}");
        var extractor = new EntityExtractor(extractorChat);

        var graph = new InMemoryGraph();
        graph.Entities.Add(new GraphEntity("acme", "Client", "Acme",
            new Dictionary<string, string> { ["industry"] = "manufacturing" }));

        // Counts only summarization calls (the prompt that carries "Subgraph:").
        var summariser = new CountingChatClient(p =>
            p.Contains("Subgraph:", StringComparison.Ordinal)
                ? "Acme is a manufacturing client."
                : "?");
        var cache = new InMemorySummaryCache();
        var lazy = new LazyGraphRagRetriever(extractor, graph, summariser, maxHops: 2, cache: cache);

        var first = await lazy.RetrieveAsync("Tell me about Acme", topK: 1);
        var second = await lazy.RetrieveAsync("What do we know about Acme?", topK: 1);

        // The subgraph is identical both times, so the LLM summarizes exactly once.
        Assert.Equal(1, summariser.SummariseCalls);
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
        Assert.Contains("manufacturing", first[0].Chunk.Text, StringComparison.Ordinal);
        Assert.Equal(first[0].Chunk.Text, second[0].Chunk.Text);
    }

    [Fact]
    public async Task LazyGraphRagRetriever_evicts_cached_summary_when_member_entity_changes()
    {
        var extractorChat = new StubChatClient(_ =>
            "{\"entities\":[{\"id\":\"acme\",\"type\":\"Client\",\"name\":\"Acme\"}]}");
        var extractor = new EntityExtractor(extractorChat);

        var graph = new InMemoryGraph();
        graph.Entities.Add(new GraphEntity("acme", "Client", "Acme",
            new Dictionary<string, string> { ["industry"] = "manufacturing" }));

        var summariser = new CountingChatClient(p =>
            p.Contains("Subgraph:", StringComparison.Ordinal)
                ? "Acme is a manufacturing client."
                : "?");
        var cache = new InMemorySummaryCache();
        var lazy = new LazyGraphRagRetriever(extractor, graph, summariser, maxHops: 2, cache: cache);

        // Prime the cache.
        _ = await lazy.RetrieveAsync("Tell me about Acme", topK: 1);
        Assert.Equal(1, summariser.SummariseCalls);

        // A member entity's facts changed — invalidate every summary built from it.
        await cache.EvictByEntityAsync("acme");

        // Next query must miss and summarize again.
        _ = await lazy.RetrieveAsync("Tell me about Acme", topK: 1);
        Assert.Equal(2, summariser.SummariseCalls);
        Assert.Equal(2, cache.Misses);
    }

    [Fact]
    public async Task CommunitySummaryIndexer_persisted_summaries_are_retrievable()
    {
        var embedder = new BagOfWordsEmbeddingGenerator();
        var embeddingService = new EmbeddingService(
            embedder, "bag-of-words-256", 256, NullLogger<EmbeddingService>.Instance);
        var store = new InMemoryVectorStore("graph-communities");
        await store.EnsureCollectionExistsAsync();

        var summaries = new List<GraphRagPipeline.CommunitySummary>
        {
            new("community-0",
                [new GraphEntity("acme", "Client", "Acme", new Dictionary<string, string>()),
                 new GraphEntity("msa", "Contract", "MSA", new Dictionary<string, string>())],
                "Acme signed the MSA contract for manufacturing services."),
            new("community-1",
                [new GraphEntity("hr", "Department", "HR", new Dictionary<string, string>()),
                 new GraphEntity("vacation", "Document", "Vacation Policy", new Dictionary<string, string>())],
                "The HR department authored the vacation policy."),
        };

        var indexer = new CommunitySummaryIndexer(embeddingService, store);
        await indexer.IndexSummariesAsync(summaries);

        var queryVector = await embeddingService.EmbedQueryAsync("vacation policy HR department");
        var hits = await store.SearchAsync(queryVector, topK: 2);

        Assert.NotEmpty(hits);
        // The HR-community summary is the top hit for an HR-flavored query.
        Assert.Equal("community/community-1#0", hits[0].Chunk.ChunkId);
        Assert.Equal("GraphSummary", hits[0].Chunk.Metadata.DocumentType);
        Assert.Contains(hits, h => h.Chunk.ChunkId == "community/community-0#0");
    }

    [Fact]
    public async Task InMemorySummaryCache_counts_hits_and_misses()
    {
        var cache = new InMemorySummaryCache();

        Assert.Null(await cache.TryGetAsync(["a", "b"]));
        Assert.Equal(0, cache.Hits);
        Assert.Equal(1, cache.Misses);

        await cache.SetAsync(["a", "b"], "summary");

        Assert.Equal("summary", await cache.TryGetAsync(["a", "b"]));
        Assert.Equal(1, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    [Fact]
    public async Task InMemorySummaryCache_key_is_stable_across_reordered_ids()
    {
        var cache = new InMemorySummaryCache();
        await cache.SetAsync(["b", "a", "c"], "summary");

        // Same set, different order (and a duplicate) -> same key -> hit.
        Assert.Equal("summary", await cache.TryGetAsync(["c", "b", "a", "a"]));
        Assert.Equal(1, cache.Hits);
        Assert.Equal(0, cache.Misses);
    }

    [Fact]
    public async Task InMemorySummaryCache_expired_entry_is_a_miss()
    {
        var cache = new InMemorySummaryCache(TimeSpan.FromMilliseconds(1));
        await cache.SetAsync(["a"], "summary");
        await Task.Delay(20);

        Assert.Null(await cache.TryGetAsync(["a"]));
        Assert.Equal(0, cache.Hits);
        Assert.Equal(1, cache.Misses);
    }

    /// <summary>
    /// <see cref="StubChatClient"/> variant that counts how many times it was
    /// asked to summarize a subgraph (a prompt containing "Subgraph:").
    /// </summary>
    private sealed class CountingChatClient : IChatClient
    {
        private readonly Func<string, string> _respond;
        private int _summariseCalls;

        public CountingChatClient(Func<string, string> respond) => _respond = respond;

        public int SummariseCalls => _summariseCalls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var text = string.Join(
                Environment.NewLine,
                messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
            if (text.Contains("Subgraph:", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _summariseCalls);
            }
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _respond(text))));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The counting stub does not stream.");

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    /// <summary>
    /// Deterministic FNV-1a bag-of-words embedder for the indexer test — the
    /// same offline stand-in the Ch 8 / Ch 16 samples use, so vector search is
    /// reproducible with no model.
    /// </summary>
    private sealed class BagOfWordsEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private const int Dimensions = 256;

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var embeddings = values
                .Select(v => new Embedding<float>(Encode(v)) { ModelId = "bag-of-words-256" })
                .ToList();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }

        private static float[] Encode(string text)
        {
            var vec = new float[Dimensions];
            foreach (var token in text.ToLowerInvariant()
                .Split([' ', '\n', '\r', '\t', ',', '.', ':', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                vec[(int)(StableHash(token) % Dimensions)] += 1f;
            }
            var mag = MathF.Sqrt(vec.Sum(v => v * v));
            if (mag > 0)
            {
                for (var i = 0; i < Dimensions; i++)
                {
                    vec[i] /= mag;
                }
            }
            return vec;
        }

        // FNV-1a (32-bit) — process-independent, never string.GetHashCode.
        private static uint StableHash(string s)
        {
            uint hash = 2166136261;
            foreach (var ch in s)
            {
                hash ^= ch;
                hash *= 16777619;
            }
            return hash;
        }
    }

    private sealed class InMemoryGraph : IGraphStore
    {
        public readonly List<GraphEntity> Entities = [];
        public Task EnsureSchemaExistsAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task UpsertEntityAsync(GraphEntity entity, CancellationToken ct = default)
        { Entities.Add(entity); return Task.CompletedTask; }
        public Task UpsertRelationAsync(GraphRelation relation, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<IReadOnlyList<IReadOnlyDictionary<string, object>>> QueryAsync(string q, IReadOnlyDictionary<string, object>? p = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object>>>([]);
        public Task<IReadOnlyList<GraphEntity>> TraverseAsync(IEnumerable<string> names, int maxHops, CancellationToken ct = default)
        {
            var lower = names.Select(n => n.ToLowerInvariant()).ToHashSet();
            IReadOnlyList<GraphEntity> hits = [.. Entities.Where(e =>
                lower.Any(n => e.Name.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                               e.Properties.Values.Any(v => v.Contains(n, StringComparison.OrdinalIgnoreCase))))];
            return Task.FromResult(hits);
        }
    }
}
