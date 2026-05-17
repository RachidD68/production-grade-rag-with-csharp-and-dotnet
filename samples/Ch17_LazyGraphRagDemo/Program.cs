// Chapter 17 — Lazy GraphRAG Demo.
//
// Demonstrates lazy vs eager GraphRAG using an in-memory knowledge graph.
// No Neo4j required. Builds a small graph (10 entities, 15 relationships),
// detects communities via connected components, then compares the token
// cost of eagerly summarizing all communities upfront vs lazily summarizing
// only on demand with caching.
//
// Run:
//   dotnet run --project samples/Ch17_LazyGraphRagDemo

// --- Build the knowledge graph ---

var nodes = new List<GraphNode>
{
    new("azure-openai", "Azure OpenAI", "Microsoft's hosted OpenAI service for enterprise AI"),
    new("embeddings", "Embeddings", "Dense vector representations of text for semantic search"),
    new("qdrant", "Qdrant", "Open-source vector database for similarity search"),
    new("rag", "RAG", "Retrieval-Augmented Generation pattern"),
    new("chunking", "Chunking", "Splitting documents into smaller retrievable units"),
    new("reranking", "Reranking", "Re-scoring retrieved results for relevance"),
    new("llm", "LLM", "Large Language Model for text generation"),
    new("prompt", "Prompt Engineering", "Crafting effective prompts for LLMs"),
    new("eval", "Evaluation", "Measuring RAG system quality with ground truth"),
    new("guardrails", "Guardrails", "Safety mechanisms preventing harmful outputs"),
};

var edges = new List<GraphEdge>
{
    new("azure-openai", "embeddings", "provides"),
    new("azure-openai", "llm", "hosts"),
    new("embeddings", "qdrant", "stored_in"),
    new("embeddings", "chunking", "requires"),
    new("rag", "embeddings", "uses"),
    new("rag", "llm", "generates_with"),
    new("rag", "reranking", "improves_with"),
    new("rag", "chunking", "depends_on"),
    new("chunking", "qdrant", "indexes_into"),
    new("reranking", "qdrant", "queries"),
    new("llm", "prompt", "configured_by"),
    new("llm", "guardrails", "constrained_by"),
    new("eval", "rag", "measures"),
    new("eval", "guardrails", "validates"),
    new("prompt", "rag", "shapes"),
};

Console.WriteLine("=== Ch17: Lazy vs Eager GraphRAG ===");
Console.WriteLine();
Console.WriteLine($"Knowledge graph: {nodes.Count} entities, {edges.Count} relationships");
Console.WriteLine();

// --- Community detection (connected components via BFS) ---

var communities = DetectCommunities(nodes, edges);
Console.WriteLine($"Detected {communities.Count} communities:");
foreach (var community in communities)
{
    var memberLabels = community.MemberIds
        .Select(id => nodes.First(n => n.Id == id).Label);
    Console.WriteLine($"  [{community.Id}] Members: {string.Join(", ", memberLabels)}");
}

Console.WriteLine();

// --- Eager approach: summarize ALL communities upfront ---

Console.WriteLine("--- Eager Approach: Summarize all communities upfront ---");
var (eagerSummaries, eagerTokens) = EagerSummarize(communities, nodes, edges);
Console.WriteLine($"  Summarized {eagerSummaries.Count} communities");
Console.WriteLine($"  Total tokens used: {eagerTokens:N0}");
foreach (var (id, summary) in eagerSummaries)
{
    Console.WriteLine($"  [{id}]: {Truncate(summary, 70)}");
}

Console.WriteLine();

// --- Lazy approach: only summarize when queried, then cache ---

Console.WriteLine("--- Lazy Approach: Summarize on demand, cache results ---");
var lazyCache = new Dictionary<string, string>();

// Simulate two queries that hit different communities.
var query1Community = communities[0].Id;
var query2Community = communities.Count > 1 ? communities[1].Id : communities[0].Id;

var (_, tokens1) = LazySummarize(query1Community, communities, nodes, edges, lazyCache);
Console.WriteLine($"  Query 1 hits [{query1Community}]: tokens used = {tokens1:N0} (cache miss)");

var (_, tokens1Cached) = LazySummarize(query1Community, communities, nodes, edges, lazyCache);
Console.WriteLine($"  Query 2 hits [{query1Community}] again: tokens used = {tokens1Cached:N0} (cache hit)");

var (_, tokens2) = LazySummarize(query2Community, communities, nodes, edges, lazyCache);
Console.WriteLine($"  Query 3 hits [{query2Community}]: tokens used = {tokens2:N0} (cache miss)");

var totalLazyTokens = tokens1 + tokens1Cached + tokens2;
Console.WriteLine();

// --- Cost comparison ---

Console.WriteLine("--- Cost Comparison ---");
Console.WriteLine($"  Eager total tokens (upfront):     {eagerTokens:N0}");
Console.WriteLine($"  Lazy total tokens (3 queries):    {totalLazyTokens:N0}");
var savings = 1.0 - ((double)totalLazyTokens / eagerTokens);
Console.WriteLine($"  Lazy savings:                     {savings:P1}");
Console.WriteLine();
Console.WriteLine("  Insight: Lazy GraphRAG avoids summarizing communities that");
Console.WriteLine("  are never queried, reducing cost for large knowledge graphs.");
return;

// --- Helpers ---

static List<Community> DetectCommunities(List<GraphNode> nodes, List<GraphEdge> edges)
{
    // Build adjacency list (undirected).
    var adj = new Dictionary<string, HashSet<string>>();
    foreach (var node in nodes)
    {
        adj[node.Id] = [];
    }

    foreach (var edge in edges)
    {
        adj[edge.SourceId].Add(edge.TargetId);
        adj[edge.TargetId].Add(edge.SourceId);
    }

    // BFS to find connected components.
    var visited = new HashSet<string>();
    var communities = new List<Community>();
    var communityIndex = 0;

    foreach (var node in nodes)
    {
        if (visited.Contains(node.Id))
        {
            continue;
        }

        var component = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(node.Id);
        visited.Add(node.Id);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            component.Add(current);

            foreach (var neighbor in adj[current])
            {
                if (visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        communities.Add(new Community($"community-{communityIndex++}", component));
    }

    return communities;
}

static (Dictionary<string, string> Summaries, int TotalTokens) EagerSummarize(
    List<Community> communities,
    List<GraphNode> nodes,
    List<GraphEdge> edges)
{
    var summaries = new Dictionary<string, string>();
    var totalTokens = 0;

    foreach (var community in communities)
    {
        var (summary, tokens) = GenerateCommunitySummary(community, nodes, edges);
        summaries[community.Id] = summary;
        totalTokens += tokens;
    }

    return (summaries, totalTokens);
}

static (string Summary, int TokensUsed) LazySummarize(
    string communityId,
    List<Community> communities,
    List<GraphNode> nodes,
    List<GraphEdge> edges,
    Dictionary<string, string> cache)
{
    if (cache.TryGetValue(communityId, out var cached))
    {
        return (cached, 0); // Cache hit — zero additional tokens.
    }

    var community = communities.First(c => c.Id == communityId);
    var (summary, tokens) = GenerateCommunitySummary(community, nodes, edges);
    cache[communityId] = summary;
    return (summary, tokens);
}

static (string Summary, int TokensUsed) GenerateCommunitySummary(
    Community community,
    List<GraphNode> nodes,
    List<GraphEdge> edges)
{
    // Simulate LLM summarization. In production, this calls the LLM.
    var memberNodes = nodes.Where(n => community.MemberIds.Contains(n.Id)).ToList();
    var relevantEdges = edges
        .Where(e => community.MemberIds.Contains(e.SourceId) && community.MemberIds.Contains(e.TargetId))
        .ToList();

    var summary = $"Community of {memberNodes.Count} entities ({string.Join(", ", memberNodes.Select(n => n.Label))}) " +
                  $"connected by {relevantEdges.Count} relationships.";

    // Estimate tokens: ~4 chars per token for input context.
    var inputContext = string.Join(" ", memberNodes.Select(n => $"{n.Label}: {n.Description}")) +
                       string.Join(" ", relevantEdges.Select(e => $"{e.SourceId} {e.Relationship} {e.TargetId}"));
    var estimatedTokens = inputContext.Length / 4 + summary.Length / 4;

    return (summary, estimatedTokens);
}

static string Truncate(string s, int max) =>
    s.Length <= max ? s : s[..max] + "...";

// --- Domain types (must follow top-level statements) ---

/// <summary>A node in the knowledge graph.</summary>
sealed record GraphNode(string Id, string Label, string Description);

/// <summary>A directed edge in the knowledge graph.</summary>
sealed record GraphEdge(string SourceId, string TargetId, string Relationship);

/// <summary>A community detected by the graph algorithm.</summary>
sealed record Community(string Id, IReadOnlyList<string> MemberIds);
