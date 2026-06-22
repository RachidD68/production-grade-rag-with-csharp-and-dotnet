using Microsoft.Extensions.AI;

namespace SmartDocs.Performance.Cost;

/// <summary>
/// A <see cref="DelegatingEmbeddingGenerator{TInput,TEmbedding}"/> that reads
/// token usage off each <see cref="GeneratedEmbeddings{TEmbedding}"/> batch and
/// records the dollar cost on the <see cref="CostMeter"/>, tagged by tenant
/// (Ch 21 cost attribution). Index-time embedding is a real, often-overlooked
/// line item; this decorator makes it visible per tenant without touching the
/// generation path. The sibling of <see cref="CostTrackingChatClient"/>.
/// </summary>
public sealed class CostTrackingEmbeddingGenerator
    : DelegatingEmbeddingGenerator<string, Embedding<float>>
{
    private readonly TokenPricing _pricing;
    private readonly string _tenant;

    /// <summary>Wrap <paramref name="inner"/>, attributing embedding spend to <paramref name="tenant"/>.</summary>
    /// <param name="inner">The embedding generator to delegate to.</param>
    /// <param name="tenant">The tenant id every cost measurement is tagged with.</param>
    /// <param name="pricing">Per-token pricing. Defaults to <see cref="TokenPricing.Default"/>.</param>
    public CostTrackingEmbeddingGenerator(
        IEmbeddingGenerator<string, Embedding<float>> inner,
        string tenant,
        TokenPricing? pricing = null)
        : base(inner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        _tenant = tenant;
        _pricing = pricing ?? TokenPricing.Default;
    }

    /// <inheritdoc />
    public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await base.GenerateAsync(values, options, cancellationToken).ConfigureAwait(false);

        var inputTokens = result.Usage?.InputTokenCount ?? result.Usage?.TotalTokenCount ?? 0;
        var cost = inputTokens * _pricing.EmbeddingPerToken;
        CostMeter.RecordCost(cost, _tenant);

        return result;
    }
}
