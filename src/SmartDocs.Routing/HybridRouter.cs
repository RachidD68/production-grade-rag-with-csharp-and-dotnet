using System.Text.RegularExpressions;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Routing;

/// <summary>
/// The route a query is sent on. One of <c>"structural"</c> (the query names a
/// formal identifier), <c>"vector"</c> (a semantic / paraphrase topic query), or
/// <c>"both"</c> (an identifier mixed with a topic — fuse the two legs).
/// </summary>
/// <param name="Destination">One of <c>"structural"</c>, <c>"vector"</c>, or <c>"both"</c>.</param>
public sealed record RouteClassification(string Destination);

/// <summary>
/// Classifies a query into a retrieval route for <see cref="HybridRouter"/>.
/// Implementations may be keyword-based (cheap, deterministic) or model-based.
/// </summary>
public interface IRouteClassifier
{
    /// <summary>Classify <paramref name="query"/> into a <see cref="RouteClassification"/>.</summary>
    Task<RouteClassification> ClassifyAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default keyword classifier: deterministic and LLM-free. A query that matches a
/// formal-identifier pattern (<c>§17</c>, <c>Article 17</c>, <c>RFC 7807</c>)
/// routes to <c>"structural"</c>; an identifier mixed with enough surrounding
/// topic words routes to <c>"both"</c>; everything else routes to <c>"vector"</c>.
/// </summary>
public sealed partial class KeywordRouteClassifier : IRouteClassifier
{
    [GeneratedRegex(@"§\s*\d+(?:\.\d+)*[a-z]?|Article\s+\d+|RFC\s+\d+", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"[\p{L}\d]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    /// <summary>
    /// Number of non-identifier words above which an identifier query is treated
    /// as an identifier+topic mix and routed to <c>"both"</c>.
    /// </summary>
    public int TopicWordThreshold { get; }

    /// <summary>Create the classifier.</summary>
    /// <param name="topicWordThreshold">
    /// Words (beyond the identifier itself) above which an identifier query is
    /// routed to <c>"both"</c> rather than <c>"structural"</c>. Default 4.
    /// </param>
    public KeywordRouteClassifier(int topicWordThreshold = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(topicWordThreshold);
        TopicWordThreshold = topicWordThreshold;
    }

    /// <inheritdoc />
    public Task<RouteClassification> ClassifyAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var idMatch = IdentifierRegex().Match(query);
        if (!idMatch.Success)
        {
            return Task.FromResult(new RouteClassification("vector"));
        }

        // Strip the identifier token(s); count what topic vocabulary remains.
        var withoutId = IdentifierRegex().Replace(query, " ");
        var topicWords = WordRegex().Count(withoutId);
        var destination = topicWords > TopicWordThreshold ? "both" : "structural";
        return Task.FromResult(new RouteClassification(destination));
    }
}

/// <summary>
/// Routes each query to the right retriever based on an <see cref="IRouteClassifier"/>:
/// identifier queries go to the structural retriever (exact), semantic queries to
/// the vector retriever (approximate), and mixed queries to a fused retriever
/// (RRF over both — typically a <c>HybridRetriever</c>). The chapter's headline
/// router: it makes the structural-vs-vector choice explicit instead of always
/// trying one then the other.
/// </summary>
public sealed class HybridRouter : IRetriever
{
    private readonly IRouteClassifier _classifier;
    private readonly IRetriever _structural;
    private readonly IRetriever _vector;
    private readonly IRetriever _fused;

    /// <summary>Create the router.</summary>
    /// <param name="classifier">Decides the route for each query.</param>
    /// <param name="structural">Retriever for identifier queries.</param>
    /// <param name="vector">Retriever for semantic / topic queries.</param>
    /// <param name="fused">Retriever for mixed queries (RRF over structural + vector).</param>
    public HybridRouter(
        IRouteClassifier classifier,
        IRetriever structural,
        IRetriever vector,
        IRetriever fused)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(structural);
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(fused);
        _classifier = classifier;
        _structural = structural;
        _vector = vector;
        _fused = fused;
    }

    /// <inheritdoc />
    public string Strategy => "hybrid";

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var route = await _classifier.ClassifyAsync(query, cancellationToken).ConfigureAwait(false);
        return route.Destination switch
        {
            "structural" => await _structural.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false),
            "vector" => await _vector.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false),
            "both" => await _fused.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false),
            _ => await _vector.RetrieveAsync(query, topK, cancellationToken).ConfigureAwait(false),
        };
    }
}
