using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Retrieval.Graph;

/// <summary>
/// LazyGraphRAG (Microsoft Research, late 2025) — defers all summarisation
/// to query time. At index time we only build the bare graph (entities +
/// relations + chunk-text mapping). At query time we:
///   1. extract query entities
///   2. traverse the graph to gather a candidate subgraph
///   3. ask the chat model to summarise just that subgraph
///
/// Result: ~0.1% of full-GraphRAG indexing cost; query cost rises a bit
/// because summarisation is now per-query.
/// </summary>
public sealed class LazyGraphRagRetriever : IRetriever
{
    private readonly EntityExtractor _extractor;
    private readonly IGraphStore _graph;
    private readonly IChatClient _chat;
    public int MaxHops { get; }

    public LazyGraphRagRetriever(EntityExtractor extractor, IGraphStore graph, IChatClient chat, int maxHops = 2)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHops);
        _extractor = extractor;
        _graph = graph;
        _chat = chat;
        MaxHops = maxHops;
    }

    public string Strategy => "lazy-graph-rag";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var extraction = await _extractor.ExtractAsync(query, cancellationToken).ConfigureAwait(false);
        var names = extraction.Entities.Select(e => e.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
        {
            return Array.Empty<RetrievalResult>();
        }

        var subgraph = await _graph.TraverseAsync(names, MaxHops, cancellationToken).ConfigureAwait(false);
        if (subgraph.Count == 0)
        {
            return Array.Empty<RetrievalResult>();
        }

        // Per-query summarisation (the "Lazy" part — no precomputed summaries).
        var subgraphText = string.Join("\n",
            subgraph.Select(e => $"- {e.Type}: {e.Name}" +
                (e.Properties.Count == 0 ? "" : " (" + string.Join("; ", e.Properties.Select(p => $"{p.Key}={p.Value}")) + ")")));

        var prompt =
            $"Given the following subgraph, summarise it as a single concise paragraph " +
            $"that would help answer the question. Reply with ONLY the summary.\n\n" +
            $"Question: {query}\n\nSubgraph:\n{subgraphText}";

        var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        var summary = (response.Text ?? string.Empty).Trim();

        var meta = new DocumentMetadata("lazy-graph", "graph", "Graph", "All",
            "Internal", "GraphSummary", 2026, "lazy-graph-rag",
            new DateOnly(2026, 1, 1), "LazyGraphRAG subgraph summary");
        var chunk = new DocumentChunk("lazy-graph#0", "lazy-graph", 0, summary, 0, summary.Length, meta);
        return [new RetrievalResult(chunk, 1.0)];
    }
}
