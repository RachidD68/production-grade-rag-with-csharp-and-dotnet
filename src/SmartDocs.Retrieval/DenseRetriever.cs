using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Retrieval;

/// <summary>
/// Dense retriever — embeds the query with the same model used at index
/// time and asks the underlying <see cref="IVectorStore"/> for the nearest
/// neighbours.
/// </summary>
public sealed class DenseRetriever : IRetriever
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly IVectorStore _store;

    public DenseRetriever(IEmbeddingGenerator<string, Embedding<float>> embeddings, IVectorStore store)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(store);
        _embeddings = embeddings;
        _store = store;
    }

    public string Strategy => "dense";

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var generated = await _embeddings.GenerateAsync(
            new[] { query }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var queryVec = generated[0].Vector;
        return await _store.SearchAsync(queryVec, topK, cancellationToken).ConfigureAwait(false);
    }
}
