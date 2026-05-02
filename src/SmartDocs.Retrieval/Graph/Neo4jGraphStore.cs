using Neo4j.Driver;

namespace SmartDocs.Retrieval.Graph;

/// <summary>
/// Neo4j adapter for <see cref="IGraphStore"/> using the official driver
/// (server CalVer 2025.x; driver SemVer 6.x). Neo4j 5.x server is what
/// the local <c>infra/docker-compose</c> brings up.
/// </summary>
public sealed class Neo4jGraphStore : IGraphStore, IAsyncDisposable
{
    private readonly IDriver _driver;

    public Neo4jGraphStore(IDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _driver = driver;
    }

    public async Task EnsureSchemaExistsAsync(CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        await session.RunAsync("CREATE CONSTRAINT entity_id IF NOT EXISTS FOR (e:Entity) REQUIRE e.id IS UNIQUE").ConfigureAwait(false);
    }

    public async Task UpsertEntityAsync(GraphEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await using var session = _driver.AsyncSession();
        var props = new Dictionary<string, object>(entity.Properties.ToDictionary(p => p.Key, p => (object)p.Value))
        {
            ["id"] = entity.Id,
            ["name"] = entity.Name,
            ["type"] = entity.Type,
        };
        await session.RunAsync(
            "MERGE (e:Entity { id: $id }) SET e += $props, e:`" + Sanitize(entity.Type) + "`",
            new { id = entity.Id, props }).ConfigureAwait(false);
    }

    public async Task UpsertRelationAsync(GraphRelation relation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relation);
        await using var session = _driver.AsyncSession();
        var rel = Sanitize(relation.Type);
        await session.RunAsync(
            $"MATCH (a:Entity {{ id: $from }}), (b:Entity {{ id: $to }}) " +
            $"MERGE (a)-[r:`{rel}`]->(b) SET r += $props",
            new { from = relation.FromId, to = relation.ToId, props = relation.Properties }).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object>>> QueryAsync(
        string query,
        IReadOnlyDictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        await using var session = _driver.AsyncSession();
        var cursor = await session.RunAsync(query, parameters as object ?? new { }).ConfigureAwait(false);
        var results = new List<IReadOnlyDictionary<string, object>>();
        await foreach (var record in cursor.ConfigureAwait(false))
        {
            results.Add(record.Values.ToDictionary(kv => kv.Key, kv => kv.Value));
        }
        return results;
    }

    public async Task<IReadOnlyList<GraphEntity>> TraverseAsync(
        IEnumerable<string> entityNames,
        int maxHops,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityNames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHops);
        var names = entityNames.ToList();
        if (names.Count == 0)
        {
            return Array.Empty<GraphEntity>();
        }

        var cypher =
            $"UNWIND $names AS name " +
            $"MATCH (start:Entity) WHERE toLower(start.name) CONTAINS toLower(name) " +
            $"OPTIONAL MATCH (start)-[*1..{maxHops}]-(neighbour:Entity) " +
            $"WITH collect(DISTINCT start) + collect(DISTINCT neighbour) AS nodes " +
            $"UNWIND nodes AS n RETURN DISTINCT n";

        var rows = await QueryAsync(cypher, new Dictionary<string, object> { ["names"] = names }, cancellationToken).ConfigureAwait(false);
        var entities = new List<GraphEntity>();
        foreach (var row in rows)
        {
            if (row["n"] is not INode node)
            {
                continue;
            }
            var props = node.Properties.Where(p => p.Key is not ("id" or "name" or "type"))
                .ToDictionary(p => p.Key, p => p.Value?.ToString() ?? string.Empty);
            entities.Add(new GraphEntity(
                Id: node.Properties.GetValueOrDefault("id")?.ToString() ?? "",
                Type: node.Properties.GetValueOrDefault("type")?.ToString() ?? "",
                Name: node.Properties.GetValueOrDefault("name")?.ToString() ?? "",
                Properties: props));
        }
        return entities;
    }

    public ValueTask DisposeAsync() => _driver.DisposeAsync();

    /// <summary>Allow only [A-Za-z0-9_] in label / relation names — the rest of Cypher is parameterised.</summary>
    private static string Sanitize(string name)
    {
        var span = name.AsSpan();
        Span<char> buf = stackalloc char[span.Length];
        int j = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsLetterOrDigit(span[i]) || span[i] == '_')
            {
                buf[j++] = span[i];
            }
        }
        return j == 0 ? "Unknown" : new string(buf[..j]);
    }
}
