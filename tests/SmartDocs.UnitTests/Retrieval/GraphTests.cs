using SmartDocs.Core.Documents;
using SmartDocs.Retrieval.Graph;

namespace SmartDocs.UnitTests.Retrieval;

public sealed class GraphTests
{
    [Fact]
    public async Task EntityExtractor_parses_LLM_json()
    {
        var stub = new StubChatClient(_ =>
            "{\"entities\":[{\"id\":\"acme\",\"type\":\"Client\",\"name\":\"Acme Corp\"}]," +
            "\"relations\":[{\"from\":\"acme\",\"to\":\"contoso\",\"type\":\"SIGNED_BY\"}]}");
        var extractor = new EntityExtractor(stub);

        var result = await extractor.ExtractAsync("Acme Corp signed with Contoso.");

        Assert.Single(result.Entities);
        Assert.Equal("Client", result.Entities[0].Type);
        Assert.Single(result.Relations);
        Assert.Equal("SIGNED_BY", result.Relations[0].Type);
    }

    [Fact]
    public async Task EntityExtractor_returns_empty_on_unparseable_response()
    {
        var stub = new StubChatClient(_ => "I have no idea");
        var extractor = new EntityExtractor(stub);

        var result = await extractor.ExtractAsync("anything");

        Assert.Empty(result.Entities);
        Assert.Empty(result.Relations);
    }

    [Fact]
    public async Task GraphRetriever_traverses_extracted_entities()
    {
        var extractorChat = new StubChatClient(_ =>
            "{\"entities\":[{\"id\":\"acme\",\"type\":\"Client\",\"name\":\"Acme\"}]}");
        var extractor = new EntityExtractor(extractorChat);

        var graph = new InMemoryGraph();
        graph.Add(new GraphEntity("acme", "Client", "Acme",
            new Dictionary<string, string> { ["industry"] = "manufacturing" }));
        graph.Add(new GraphEntity("contract-001", "Contract", "MSA-2026",
            new Dictionary<string, string> { ["counterparty"] = "Acme" }));

        var retriever = new GraphRetriever(extractor, graph);
        var hits = await retriever.RetrieveAsync("Tell me about Acme", topK: 5);

        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Chunk.Text.Contains("Acme", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DocumentToGraphPipeline_dedupes_entities_by_name()
    {
        var extractorChat = new StubChatClient(p =>
            p.Contains("Acme signed", StringComparison.Ordinal)
                ? "{\"entities\":[{\"id\":\"acme-1\",\"type\":\"Client\",\"name\":\"Acme\"}]}"
                : "{\"entities\":[{\"id\":\"acme-2\",\"type\":\"Client\",\"name\":\"Acme\"}]}");
        var extractor = new EntityExtractor(extractorChat);
        var graph = new InMemoryGraph();
        var pipeline = new DocumentToGraphPipeline(extractor, graph);

        var meta = new DocumentMetadata("d", "x", "x", "x", "Internal", "Policy",
            2026, "x", new DateOnly(2026, 1, 1), "x");
        await pipeline.IngestAsync(new[]
        {
            new DocumentChunk("c1", "d", 0, "Acme signed the agreement.", 0, 26, meta),
            new DocumentChunk("c2", "d", 1, "Acme renewed today.",         0, 19, meta),
        });

        // Acme deduped to a single entity (the first canonical id).
        Assert.Single(graph.Entities, e => e.Name == "Acme");
    }

    private sealed class InMemoryGraph : IGraphStore
    {
        public readonly List<GraphEntity> Entities = [];
        public readonly List<GraphRelation> Relations = [];
        public Task EnsureSchemaExistsAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Add(GraphEntity e) => Entities.Add(e);
        public Task UpsertEntityAsync(GraphEntity entity, CancellationToken ct = default)
        {
            Entities.RemoveAll(e => e.Id == entity.Id);
            Entities.Add(entity);
            return Task.CompletedTask;
        }
        public Task UpsertRelationAsync(GraphRelation r, CancellationToken ct = default)
        {
            Relations.Add(r); return Task.CompletedTask;
        }
        public Task<IReadOnlyList<IReadOnlyDictionary<string, object>>> QueryAsync(
            string query, IReadOnlyDictionary<string, object>? p = null, CancellationToken ct = default)
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
