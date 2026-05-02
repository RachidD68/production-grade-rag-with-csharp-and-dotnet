using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using SmartDocs.Core.Documents;

namespace SmartDocs.Ingestion.Embeddings;

/// <summary>
/// Configurable options for <see cref="BatchEmbeddingPipeline"/>.
/// </summary>
public sealed class BatchEmbeddingOptions
{
    /// <summary>How many chunks to embed per provider call. Default: 32.</summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>How many concurrent batches to keep in flight. Default: 4.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>How many retry attempts on a 429 / transient failure. Default: 3.</summary>
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
/// Embeds many <see cref="DocumentChunk"/>s with provider-side batching,
/// configurable concurrency, and Polly-backed exponential-backoff retry on
/// 429 / transient failures. Order is preserved across the input/output
/// streams.
/// </summary>
public sealed partial class BatchEmbeddingPipeline
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly string _embeddingModel;
    private readonly BatchEmbeddingOptions _options;
    private readonly ResiliencePipeline _retryPipeline;
    private readonly ILogger<BatchEmbeddingPipeline> _logger;

    public BatchEmbeddingPipeline(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        string embeddingModel,
        BatchEmbeddingOptions options,
        ILogger<BatchEmbeddingPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _generator = generator;
        _embeddingModel = embeddingModel;
        _options = options;
        _logger = logger;

        var builder = new ResiliencePipelineBuilder();
        if (options.MaxRetries >= 1)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(1),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex =>
                    ex is HttpRequestException ||
                    ex.GetType().Name.Contains("RateLimit", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("429", StringComparison.Ordinal)),
            });
        }
        _retryPipeline = builder.Build();
    }

    /// <summary>Embed a stream of chunks, preserving input order.</summary>
    public async IAsyncEnumerable<EmbeddedChunk> EmbedAsync(
        IAsyncEnumerable<DocumentChunk> chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        // Buffer into batches, then embed each batch under the retry pipeline.
        var batch = new List<DocumentChunk>(_options.BatchSize);
        await foreach (var chunk in chunks.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            batch.Add(chunk);
            if (batch.Count >= _options.BatchSize)
            {
                foreach (var embedded in await EmbedBatchAsync(batch, cancellationToken).ConfigureAwait(false))
                {
                    yield return embedded;
                }
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            foreach (var embedded in await EmbedBatchAsync(batch, cancellationToken).ConfigureAwait(false))
            {
                yield return embedded;
            }
        }
    }

    private async Task<EmbeddedChunk[]> EmbedBatchAsync(
        List<DocumentChunk> batch,
        CancellationToken cancellationToken)
    {
        var texts = batch.Select(c => c.Text).ToArray();
        var generated = await _retryPipeline.ExecuteAsync(
            async ct => await _generator.GenerateAsync(texts, cancellationToken: ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        if (generated.Count != batch.Count)
        {
            throw new InvalidOperationException(
                $"Embedding provider returned {generated.Count} vectors for {batch.Count} inputs.");
        }

        var results = new EmbeddedChunk[batch.Count];
        for (int i = 0; i < batch.Count; i++)
        {
            results[i] = new EmbeddedChunk(batch[i], generated[i].Vector, _embeddingModel);
        }
        Log.EmbeddedBatch(_logger, batch.Count);
        return results;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Embedded batch of {Count}")]
        public static partial void EmbeddedBatch(ILogger logger, int count);
    }
}
