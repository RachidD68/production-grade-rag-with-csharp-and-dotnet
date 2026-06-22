using Microsoft.Extensions.AI;

namespace SmartDocs.Performance.Cost;

/// <summary>
/// A <see cref="DelegatingChatClient"/> that reads token usage off each
/// <see cref="ChatResponse"/> and records the dollar cost on the
/// <see cref="CostMeter"/>, tagged by tenant (Ch 21 cost attribution). It is a
/// pure decorator — it never changes the request or the response, so it can be
/// layered anywhere in an <see cref="IChatClient"/> pipeline. Follows the
/// <c>EmbeddingCache</c> decorator precedent: wrap one inner collaborator, add a
/// single cross-cutting concern, leave the contract untouched.
/// </summary>
public sealed class CostTrackingChatClient : DelegatingChatClient
{
    private readonly TokenPricing _pricing;
    private readonly string _tenant;

    /// <summary>Wrap <paramref name="inner"/>, attributing spend to <paramref name="tenant"/>.</summary>
    /// <param name="inner">The chat client to delegate to.</param>
    /// <param name="tenant">The tenant id every cost measurement is tagged with.</param>
    /// <param name="pricing">Per-token pricing. Defaults to <see cref="TokenPricing.Default"/>.</param>
    public CostTrackingChatClient(IChatClient inner, string tenant, TokenPricing? pricing = null)
        : base(inner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        _tenant = tenant;
        _pricing = pricing ?? TokenPricing.Default;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        RecordCost(response.Usage);
        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Usage normally arrives on the final streamed update; accumulate any
        // UsageContent we see and bill once the stream completes.
        UsageDetails? usage = null;
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent uc)
                {
                    usage ??= new UsageDetails();
                    usage.Add(uc.Details);
                }
            }

            yield return update;
        }

        RecordCost(usage);
    }

    private void RecordCost(UsageDetails? usage)
    {
        if (usage is null)
        {
            return;
        }
        var input = usage.InputTokenCount ?? 0;
        var output = usage.OutputTokenCount ?? 0;
        var cost = (input * _pricing.InputPerToken) + (output * _pricing.OutputPerToken);
        CostMeter.RecordCost(cost, _tenant);
    }
}
