using Microsoft.Extensions.AI;

namespace SmartDocs.Performance.Routing;

/// <summary>
/// An <see cref="IChatClient"/> that routes to a provisioned-throughput (PTU)
/// backend until it saturates, then overflows to a pay-as-you-go (PAYG) backend
/// (Ch 21 capacity routing). PTU capacity is prepaid and effectively free at the
/// margin, so you want every request you can fit to land there; once the PTU
/// deployment is at its concurrency ceiling, spilling the overflow to PAYG keeps
/// latency bounded instead of queueing. "Queue depth" here is the count of
/// in-flight PTU calls; when it reaches <see cref="QueueDepthThreshold"/> a new
/// call overflows to PAYG.
/// <para>
/// Streaming is implemented too (<see cref="GetStreamingResponseAsync"/>), not
/// just the one-shot path, so the router is a drop-in for the streaming pipeline.
/// </para>
/// </summary>
public sealed class MultiBackendChatClient : IChatClient
{
    private readonly IChatClient _ptu;
    private readonly IChatClient _payg;
    private int _ptuInFlight;

    /// <summary>The in-flight PTU call count at or above which new calls overflow to PAYG.</summary>
    public int QueueDepthThreshold { get; }

    /// <summary>Create a router over a PTU and a PAYG backend.</summary>
    /// <param name="ptuClient">The provisioned-throughput backend (preferred while it has headroom).</param>
    /// <param name="paygClient">The pay-as-you-go overflow backend.</param>
    /// <param name="queueDepthThreshold">Max concurrent PTU calls before overflow. Must be positive.</param>
    public MultiBackendChatClient(IChatClient ptuClient, IChatClient paygClient, int queueDepthThreshold = 4)
    {
        ArgumentNullException.ThrowIfNull(ptuClient);
        ArgumentNullException.ThrowIfNull(paygClient);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(queueDepthThreshold);
        _ptu = ptuClient;
        _payg = paygClient;
        QueueDepthThreshold = queueDepthThreshold;
    }

    /// <summary>The number of PTU calls currently in flight. Exposed for tests and metrics.</summary>
    public int PtuInFlight => Volatile.Read(ref _ptuInFlight);

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Reserve a PTU slot if there is headroom; otherwise overflow to PAYG.
        if (TryEnterPtu())
        {
            try
            {
                return await _ptu.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _ptuInFlight);
            }
        }

        return await _payg.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (TryEnterPtu())
        {
            try
            {
                await foreach (var update in _ptu.GetStreamingResponseAsync(messages, options, cancellationToken)
                    .ConfigureAwait(false))
                {
                    yield return update;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _ptuInFlight);
            }

            yield break;
        }

        await foreach (var update in _payg.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : _ptu.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _ptu.Dispose();
        _payg.Dispose();
    }

    // Atomically claim a PTU slot if in-flight count is below the threshold.
    private bool TryEnterPtu()
    {
        while (true)
        {
            var current = Volatile.Read(ref _ptuInFlight);
            if (current >= QueueDepthThreshold)
            {
                return false;
            }
            if (Interlocked.CompareExchange(ref _ptuInFlight, current + 1, current) == current)
            {
                return true;
            }
        }
    }
}
