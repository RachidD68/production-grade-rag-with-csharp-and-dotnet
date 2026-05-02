using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Retrieval.Vectorless;

/// <summary>
/// Vectorless RAG — uses an <see cref="IChatClient"/> to classify the
/// query against the document structure (titles + paths only) and
/// returns the matching section's body. Wins on hierarchical corpora
/// (legal contracts: Article > Section > Clause; technical manuals;
/// regulatory filings) where structural location is the right granularity.
/// </summary>
public sealed class VectorlessRetriever : IRetriever
{
    private readonly StructuralIndex _index;
    private readonly IChatClient _chat;

    public VectorlessRetriever(StructuralIndex index, IChatClient chat)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(chat);
        _index = index;
        _chat = chat;
    }

    public string Strategy => "vectorless";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var nodes = _index.Root.AllNodes().ToList();
        var titles = string.Join("\n",
            nodes.Select((n, i) => $"{i}: {n.Path}"));

        var prompt =
            $"You are routing a question to the most relevant section(s) of a structured document. " +
            $"Reply with a comma-separated list of up to {topK} integer indices, in order of relevance. " +
            $"No prose.\n\n" +
            $"Sections:\n{titles}\n\n" +
            $"Question: {query}";

        var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        var raw = response.Text ?? string.Empty;
        var picks = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => int.TryParse(t, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : -1)
            .Where(n => n >= 0 && n < nodes.Count)
            .Distinct()
            .Take(topK)
            .ToList();

        var meta = new DocumentMetadata("structural", "structural", "Structural", "All",
            "Internal", "Section", 2026, "structural",
            new DateOnly(2026, 1, 1), _index.Root.Title);

        return [.. picks
            .Select((idx, rank) =>
            {
                var node = nodes[idx];
                var text = $"{node.Path}\n\n{node.Body}";
                var chunk = new DocumentChunk($"struct#{idx}", "structural", rank, text, 0, text.Length, meta);
                return new RetrievalResult(chunk, 1.0 / (rank + 1));
            })];
    }
}

/// <summary>
/// Tries the vectorless retriever first; if it returns nothing, falls back
/// to the supplied dense / hybrid retriever.
/// </summary>
public sealed class StructuralVectorRetriever : IRetriever
{
    private readonly VectorlessRetriever _vectorless;
    private readonly IRetriever _vectorFallback;

    public StructuralVectorRetriever(VectorlessRetriever vectorless, IRetriever vectorFallback)
    {
        ArgumentNullException.ThrowIfNull(vectorless);
        ArgumentNullException.ThrowIfNull(vectorFallback);
        _vectorless = vectorless;
        _vectorFallback = vectorFallback;
    }

    public string Strategy => $"structural-then-{_vectorFallback.Strategy}";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query, int topK, CancellationToken cancellationToken = default)
    {
        var hits = await _vectorless.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false);
        if (hits.Count > 0)
        {
            return hits;
        }

        return await _vectorFallback.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false);
    }
}
