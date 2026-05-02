using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Reranking;

/// <summary>
/// IRetriever decorator that runs the inner retriever with a wider
/// candidate count, then reranks down to the user's requested topK.
/// Implements the production-default
/// <em>retrieve-broadly, rerank-precisely</em> pattern.
/// </summary>
public sealed class RerankingMiddleware : IRetriever
{
    private readonly IRetriever _inner;
    private readonly IReranker _reranker;
    public int CandidateCount { get; }

    public RerankingMiddleware(IRetriever inner, IReranker reranker, int candidateCount = 20)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(reranker);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateCount);
        _inner = inner;
        _reranker = reranker;
        CandidateCount = candidateCount;
    }

    public string Strategy => $"{_inner.Strategy}+rerank({_reranker.Implementation})";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var candidateK = Math.Max(CandidateCount, topK);
        var candidates = await _inner.RetrieveAsync(query, candidateK, cancellationToken).ConfigureAwait(false);
        return await _reranker.RerankAsync(query, candidates, topK, cancellationToken).ConfigureAwait(false);
    }
}
