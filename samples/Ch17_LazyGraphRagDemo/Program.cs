// Chapter 17 — LazyGraphRAG Demo.
//
// Demonstrates LazyGraphRAG end-to-end against the real library types
// (SmartDocs.Retrieval.Graph.LazyGraphRagRetriever + InMemorySummaryCache),
// fully offline — no Neo4j, no API key. A deterministic stub IChatClient stands
// in for the summarisation LLM and counts how many times it is actually called,
// and a tiny in-memory IGraphStore returns the subgraph for "Acme".
//
// The point of the chapter: LazyGraphRAG defers summarisation to query time, and
// an ISummaryCache makes that per-query cost bounded. We run the SAME query
// twice; the second run resolves to the same subgraph, hits the cache, and skips
// the LLM call entirely. The console prints cache Hits / Misses and the LLM call
// count so the economics are visible, not asserted.
//
// Run:
//   dotnet run --project samples/Ch17_LazyGraphRagDemo

using Microsoft.Extensions.AI;
using SmartDocs.Retrieval.Graph;

Console.WriteLine("=== Ch17: LazyGraphRAG — query-time summaries with caching ===");
Console.WriteLine();

// --- The in-memory knowledge graph (no Neo4j). ---
// A small subgraph about a client "Acme" and its contract.
var graph = new InMemoryGraph();
graph.Add(new GraphEntity("acme", "Client", "Acme",
    new Dictionary<string, string> { ["industry"] = "manufacturing" }));
graph.Add(new GraphEntity("msa-2026", "Contract", "MSA 2026",
    new Dictionary<string, string> { ["counterparty"] = "Acme", ["value"] = "1.2M" }));
graph.Add(new GraphEntity("paris-office", "Office", "Paris",
    new Dictionary<string, string> { ["serves"] = "Acme" }));

Console.WriteLine($"Knowledge graph: {graph.Count} entities (in-memory, no Neo4j).");
Console.WriteLine();

// --- The summarisation LLM (stubbed, deterministic, call-counting). ---
// In production this is a real IChatClient (Azure OpenAI, Ollama, ...). Here it
// returns a fixed summary and counts every summarise call so we can SEE the
// cache avoid the second one.
var llm = new CountingChatClient(prompt =>
    prompt.Contains("Subgraph:", StringComparison.Ordinal)
        ? "Acme is a manufacturing client served by the Paris office under the MSA 2026 contract."
        : "{\"entities\":[{\"id\":\"acme\",\"type\":\"Client\",\"name\":\"Acme\"}]}");

// EntityExtractor uses the same stub to turn the query into seed entities.
var extractor = new EntityExtractor(llm);

// The cache that makes the LazyGraphRAG economics real. In production this is an
// IDistributedCache / Redis adapter (Ch 21); here it is the in-memory dev seam.
var cache = new InMemorySummaryCache();

var retriever = new LazyGraphRagRetriever(extractor, graph, llm, maxHops: 2, cache: cache);

// --- Run the SAME question twice. ---
Console.WriteLine("--- Query 1 (cold cache) ---");
var first = await retriever.RetrieveAsync("Tell me about Acme", topK: 1).ConfigureAwait(false);
Console.WriteLine($"  Summary: {first[0].Chunk.Text}");
Console.WriteLine($"  LLM summarise calls: {llm.SummariseCalls} | cache Hits={cache.Hits} Misses={cache.Misses}");
Console.WriteLine();

Console.WriteLine("--- Query 2 (same subgraph, warm cache) ---");
var second = await retriever.RetrieveAsync("What do we know about Acme?", topK: 1).ConfigureAwait(false);
Console.WriteLine($"  Summary: {second[0].Chunk.Text}");
Console.WriteLine($"  LLM summarise calls: {llm.SummariseCalls} | cache Hits={cache.Hits} Misses={cache.Misses}");
Console.WriteLine();

// --- What the numbers mean. ---
Console.WriteLine("--- Result ---");
Console.WriteLine($"  Two queries resolved to the same subgraph.");
Console.WriteLine($"  The LLM summarised only {llm.SummariseCalls} time(s); query 2 was served from cache.");
Console.WriteLine($"  cache Hits={cache.Hits}, Misses={cache.Misses}.");
Console.WriteLine();
Console.WriteLine("  Insight: LazyGraphRAG pays for summarisation per query, but caching");
Console.WriteLine("  by subgraph means equivalent questions cost nothing extra — the");
Console.WriteLine("  economics the chapter claims, made true and offline-testable.");
return;

// --- Offline helpers (must follow top-level statements) ---

/// <summary>
/// Deterministic, call-counting <see cref="IChatClient"/>. Replies via a
/// caller-supplied function and counts summarise calls (prompts that carry
/// "Subgraph:") so the demo can show the cache avoiding the LLM.
/// </summary>
internal sealed class CountingChatClient(Func<string, string> respond) : IChatClient
{
    private int _summariseCalls;

    public int SummariseCalls => _summariseCalls;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var text = string.Join(
            Environment.NewLine,
            messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        if (text.Contains("Subgraph:", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _summariseCalls);
        }
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, respond(text))));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The offline demo does not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        // Nothing to dispose.
    }
}

/// <summary>
/// Minimal in-memory <see cref="IGraphStore"/>: traversal returns every entity
/// whose name or property values mention one of the query's seed names. Stands
/// in for Neo4j so the demo runs with no database.
/// </summary>
internal sealed class InMemoryGraph : IGraphStore
{
    private readonly List<GraphEntity> _entities = [];

    public int Count => _entities.Count;

    public void Add(GraphEntity entity) => _entities.Add(entity);

    public Task EnsureSchemaExistsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UpsertEntityAsync(GraphEntity entity, CancellationToken cancellationToken = default)
    {
        _entities.Add(entity);
        return Task.CompletedTask;
    }

    public Task UpsertRelationAsync(GraphRelation relation, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object>>> QueryAsync(
        string query,
        IReadOnlyDictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object>>>([]);

    public Task<IReadOnlyList<GraphEntity>> TraverseAsync(
        IEnumerable<string> entityNames,
        int maxHops,
        CancellationToken cancellationToken = default)
    {
        var seeds = entityNames.Select(n => n.ToLowerInvariant()).ToHashSet();
        IReadOnlyList<GraphEntity> hits = [.. _entities.Where(e =>
            seeds.Any(n => e.Name.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                           e.Properties.Values.Any(v => v.Contains(n, StringComparison.OrdinalIgnoreCase))))];
        return Task.FromResult(hits);
    }
}
