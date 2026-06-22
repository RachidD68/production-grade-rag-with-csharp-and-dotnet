using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using SmartDocs.Generation;

namespace SmartDocs.Performance;

/// <summary>
/// Layer 1 of the caching stack (Ch 21): a full-response cache. Decorates an
/// inner <see cref="IRagPipeline"/> and, keyed on the normalised question,
/// serves the entire <see cref="RagResponse"/> from an
/// <see cref="IDistributedCache"/> on a repeat — skipping retrieval,
/// augmentation, and generation altogether. An exact-query hit is the cheapest
/// possible answer: zero token cost, sub-millisecond latency.
/// <para>
/// Backed by <see cref="IDistributedCache"/> (Redis in production) rather than a
/// process-local cache, so the hit is shared across every instance behind the
/// load balancer — the cost math in the chapter assumes cross-instance hits.
/// Mirrors the <see cref="EmbeddingCache"/> decorator precedent: a sealed
/// wrapper over a single inner collaborator with a hit/miss path and a test.
/// </para>
/// </summary>
public sealed class ResponseCache : IRagPipeline
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IRagPipeline _inner;
    private readonly IDistributedCache _cache;

    /// <summary>The default time-to-live applied to cached responses.</summary>
    public TimeSpan Ttl { get; }

    /// <summary>Create a response cache over <paramref name="inner"/> backed by <paramref name="cache"/>.</summary>
    /// <param name="inner">The pipeline to invoke on a cache miss.</param>
    /// <param name="cache">The distributed cache holding serialised responses.</param>
    /// <param name="ttl">Time-to-live for cached entries. Defaults to 10 minutes.</param>
    public ResponseCache(IRagPipeline inner, IDistributedCache cache, TimeSpan? ttl = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        _inner = inner;
        _cache = cache;
        Ttl = ttl ?? TimeSpan.FromMinutes(10);
    }

    /// <inheritdoc />
    public async Task<RagResponse> AskAsync(string question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var key = KeyFor(question);

        var hit = await TryGetAsync(key, ct).ConfigureAwait(false);
        if (hit is not null)
        {
            return hit;
        }

        var fresh = await _inner.AskAsync(question, ct).ConfigureAwait(false);
        await SetAsync(key, fresh, ct).ConfigureAwait(false);
        return fresh;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RagStreamEvent> AskStreamingAsync(
        string question,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var key = KeyFor(question);

        var hit = await TryGetAsync(key, ct).ConfigureAwait(false);
        if (hit is not null)
        {
            // Replay the streaming contract from the cached response so the SSE
            // client cannot tell a hit from a miss: Sources, then a single Token
            // carrying the whole answer, then Done.
            yield return new RagStreamEvent(RagStreamEventKind.Sources, Sources: hit.Sources);
            yield return new RagStreamEvent(RagStreamEventKind.Token, Token: hit.Answer);
            yield return new RagStreamEvent(RagStreamEventKind.Done);
            yield break;
        }

        // Miss: stream the inner pipeline through to the client while
        // reassembling the answer so we can populate the cache when it finishes.
        var answer = new StringBuilder();
        IReadOnlyList<Core.Documents.RetrievalResult> sources = [];
        var faulted = false;

        await foreach (var ev in _inner.AskStreamingAsync(question, ct).ConfigureAwait(false))
        {
            switch (ev.Kind)
            {
                case RagStreamEventKind.Sources when ev.Sources is not null:
                    sources = ev.Sources;
                    break;
                case RagStreamEventKind.Token when ev.Token is not null:
                    answer.Append(ev.Token);
                    break;
                case RagStreamEventKind.Error:
                    // Never cache a failed generation.
                    faulted = true;
                    break;
                default:
                    break;
            }

            yield return ev;
        }

        if (!faulted)
        {
            var assembled = new RagResponse(answer.ToString(), sources, LatencyMs: 0, Strategy: "cached");
            await SetAsync(key, assembled, ct).ConfigureAwait(false);
        }
    }

    private async Task<RagResponse?> TryGetAsync(string key, CancellationToken ct)
    {
        var bytes = await _cache.GetAsync(key, ct).ConfigureAwait(false);
        return bytes is null ? null : JsonSerializer.Deserialize<RagResponse>(bytes, SerializerOptions);
    }

    private Task SetAsync(string key, RagResponse response, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, SerializerOptions);
        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl };
        return _cache.SetAsync(key, bytes, options, ct);
    }

    private static string KeyFor(string question) => "resp:" + question.Trim().ToLowerInvariant();
}
