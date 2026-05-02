using SmartDocs.Core.Documents;
using SmartDocs.Retrieval.Graph;

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
