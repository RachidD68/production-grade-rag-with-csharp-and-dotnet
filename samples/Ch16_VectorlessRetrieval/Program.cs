// Chapter 16 — Vectorless Retrieval.
//
// Demonstrates structural/vectorless retrieval on a markdown specification.
// Builds a StructuralNode tree from a sample markdown document, then shows
// how section-based lookup, tree traversal, and cross-reference following
// can answer queries that pure vector search would struggle with.
//
// Run:
//   dotnet run --project samples/Ch16_VectorlessRetrieval

// --- Sample markdown document (API specification) ---

const string SampleMarkdown = """
    # API Specification v2.1

    ## Authentication
    All endpoints require Bearer token authentication.
    Tokens expire after 3600 seconds.
    See [Rate Limiting](#rate-limiting) for throttling details.

    ### OAuth2 Flow
    Use the /oauth/token endpoint with client_credentials grant.
    Refresh tokens are valid for 30 days.

    ### API Keys
    Legacy API keys are deprecated as of v2.0.
    Migration deadline: 2026-06-01.
    See [Authentication](#authentication) for the recommended approach.

    ## Rate Limiting
    Default rate limit: 1000 requests per minute.
    Burst allowance: 50 requests per second.
    See [Authentication](#authentication) for token-based rate tiers.

    ### Enterprise Tier
    Enterprise clients get 10,000 requests per minute.
    Contact sales for custom limits.

    ## Error Codes
    All errors follow RFC 7807 Problem Details format.

    ### 429 Too Many Requests
    Returned when rate limit is exceeded.
    Retry-After header indicates wait time in seconds.
    See [Rate Limiting](#rate-limiting) for limit details.
    """;

// --- Parse markdown into structural tree ---

var nodes = ParseMarkdown(SampleMarkdown);

Console.WriteLine("=== Ch16: Vectorless Retrieval ===");
Console.WriteLine();
Console.WriteLine($"Parsed {nodes.Count} structural nodes:");
foreach (var node in nodes.Values)
{
    var indent = node.Type == "h3" ? "    " : node.Type == "h2" ? "  " : "";
    Console.WriteLine($"{indent}[{node.Id}] ({node.Type}) {node.Title}");
}

// --- Demonstrate retrieval strategies ---

Console.WriteLine();
Console.WriteLine("--- Strategy 1: Lookup by Section ID ---");
var target = LookupById(nodes, "rate-limiting");
if (target is not null)
{
    Console.WriteLine($"  Found: [{target.Id}] {target.Title}");
    Console.WriteLine($"  Content: {target.Content}");
}

Console.WriteLine();
Console.WriteLine("--- Strategy 2: Tree Traversal (children of 'authentication') ---");
var children = GetChildren(nodes, "authentication");
foreach (var child in children)
{
    Console.WriteLine($"  [{child.Id}] {child.Title}: {Truncate(child.Content, 60)}");
}

Console.WriteLine();
Console.WriteLine("--- Strategy 3: Cross-Reference Following ---");
Console.WriteLine("  Starting at '429-too-many-requests', following cross-refs:");
var visited = FollowCrossRefs(nodes, "429-too-many-requests", maxDepth: 3);
foreach (var (node, depth) in visited)
{
    var indent = new string(' ', depth * 2 + 2);
    Console.WriteLine($"{indent}[depth={depth}] [{node.Id}] {node.Title}");
}

Console.WriteLine();
Console.WriteLine("--- Why this beats vector search ---");
Console.WriteLine("  Query: 'What is the rate limit for enterprise clients?'");
Console.WriteLine("  Vector search might return the general 'Rate Limiting' section.");
Console.WriteLine("  Structural retrieval: navigate to 'rate-limiting' -> child 'enterprise-tier':");
var rateLimiting = LookupById(nodes, "rate-limiting");
if (rateLimiting is not null)
{
    var enterpriseChildren = GetChildren(nodes, "rate-limiting");
    var enterprise = enterpriseChildren.FirstOrDefault(c => c.Id == "enterprise-tier");
    if (enterprise is not null)
    {
        Console.WriteLine($"  Direct answer: {enterprise.Content}");
    }
}

Console.WriteLine();
Console.WriteLine("Done.");
return;

// --- Parser ---

static Dictionary<string, StructuralNode> ParseMarkdown(string markdown)
{
    var nodes = new Dictionary<string, StructuralNode>();
    var lines = markdown.Split('\n', StringSplitOptions.TrimEntries);

    string? currentId = null;
    string? currentType = null;
    string? currentTitle = null;
    string? currentParentId = null;
    var contentLines = new List<string>();
    var crossRefs = new List<string>();

    foreach (var line in lines)
    {
        if (line.StartsWith("# ", StringComparison.Ordinal) || line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
        {
            // Flush previous node.
            if (currentId is not null)
            {
                FlushNode(nodes, currentId, currentType!, currentTitle!, currentParentId, contentLines, crossRefs);
            }

            // Parse heading.
            var level = line.TakeWhile(c => c == '#').Count();
            currentType = $"h{level}";
            currentTitle = line[(level + 1)..].Trim();
            currentId = Slugify(currentTitle);
            currentParentId = level switch
            {
                3 => FindParentH2(nodes),
                2 => FindRootId(nodes),
                _ => null,
            };
            contentLines.Clear();
            crossRefs.Clear();
        }
        else if (!string.IsNullOrWhiteSpace(line))
        {
            contentLines.Add(line);

            // Extract cross-references like [text](#anchor).
            var idx = 0;
            while ((idx = line.IndexOf("](#", idx, StringComparison.Ordinal)) >= 0)
            {
                var end = line.IndexOf(')', idx + 3);
                if (end > idx + 3)
                {
                    crossRefs.Add(line[(idx + 3)..end]);
                }

                idx = end > 0 ? end : idx + 1;
            }
        }
    }

    // Flush last node.
    if (currentId is not null)
    {
        FlushNode(nodes, currentId, currentType!, currentTitle!, currentParentId, contentLines, crossRefs);
    }

    return nodes;
}

static void FlushNode(
    Dictionary<string, StructuralNode> nodes,
    string id, string type, string title, string? parentId,
    List<string> contentLines, List<string> crossRefs)
{
    var node = new StructuralNode(
        id, type, title,
        string.Join(" ", contentLines),
        parentId,
        [],
        [.. crossRefs]);
    nodes[id] = node;

    // Register as child of parent.
    if (parentId is not null && nodes.TryGetValue(parentId, out var parent))
    {
        parent.Children.Add(id);
    }
}

static string? FindParentH2(Dictionary<string, StructuralNode> nodes) =>
    nodes.Values.LastOrDefault(n => n.Type == "h2")?.Id;

static string? FindRootId(Dictionary<string, StructuralNode> nodes) =>
    nodes.Values.FirstOrDefault(n => n.Type == "h1")?.Id;

static string Slugify(string title) =>
    title.ToLowerInvariant()
        .Replace(' ', '-')
        .Replace(".", "")
        .Replace(",", "");

// --- Retriever ---

static StructuralNode? LookupById(Dictionary<string, StructuralNode> nodes, string id) =>
    nodes.GetValueOrDefault(id);

static IReadOnlyList<StructuralNode> GetChildren(Dictionary<string, StructuralNode> nodes, string parentId)
{
    if (!nodes.TryGetValue(parentId, out var parent))
    {
        return [];
    }

    return parent.Children
        .Where(nodes.ContainsKey)
        .Select(cid => nodes[cid])
        .ToList();
}

static IReadOnlyList<(StructuralNode Node, int Depth)> FollowCrossRefs(
    Dictionary<string, StructuralNode> nodes,
    string startId,
    int maxDepth)
{
    var results = new List<(StructuralNode, int)>();
    var visitedSet = new HashSet<string>();
    FollowRecursive(nodes, startId, 0, maxDepth, visitedSet, results);
    return results;
}

static void FollowRecursive(
    Dictionary<string, StructuralNode> nodes,
    string id,
    int depth,
    int maxDepth,
    HashSet<string> visitedSet,
    List<(StructuralNode, int)> results)
{
    if (depth > maxDepth || !visitedSet.Add(id) || !nodes.TryGetValue(id, out var node))
    {
        return;
    }

    results.Add((node, depth));

    foreach (var crossRef in node.CrossRefs)
    {
        FollowRecursive(nodes, crossRef, depth + 1, maxDepth, visitedSet, results);
    }
}

static string Truncate(string s, int max) =>
    s.Length <= max ? s : s[..max] + "...";

// --- Domain model (must follow top-level statements) ---

/// <summary>A node in the structural document tree.</summary>
sealed record StructuralNode(
    string Id,
    string Type,
    string Title,
    string Content,
    string? ParentId,
    List<string> Children,
    List<string> CrossRefs);
