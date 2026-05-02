using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Ingestion.Embeddings;

/// <summary>
/// Default <see cref="IEmbeddingService"/> implementation. Wraps an
/// <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> from
/// Microsoft.Extensions.AI and adds the SmartDocs domain concerns:
/// model-name capture on every <see cref="EmbeddedChunk"/> and a
/// streaming <see cref="EmbedAsync(IAsyncEnumerable{DocumentChunk}, CancellationToken)"/>
/// overload (the batched, rate-limited variant lives in
/// <see cref="BatchEmbeddingPipeline"/>).
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly ILogger<EmbeddingService> _logger;

    /// <summary>Create the service over an <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>.</summary>
    public EmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        string embeddingModel,
        int dimensions,
        ILogger<EmbeddingService> logger)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimensions);
        ArgumentNullException.ThrowIfNull(logger);

        _generator = generator;
        EmbeddingModel = embeddingModel;
        Dimensions = dimensions;
        _logger = logger;
    }

    /// <inheritdoc />
    public string EmbeddingModel { get; }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public async Task<EmbeddedChunk> EmbedAsync(DocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var generated = await _generator.GenerateAsync([chunk.Text], cancellationToken: cancellationToken);
        return new EmbeddedChunk(chunk, generated[0].Vector, EmbeddingModel);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<EmbeddedChunk> EmbedAsync(
        IAsyncEnumerable<DocumentChunk> chunks,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        await foreach (var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return await EmbedAsync(chunk, cancellationToken).ConfigureAwait(false);
        }
    }
}
