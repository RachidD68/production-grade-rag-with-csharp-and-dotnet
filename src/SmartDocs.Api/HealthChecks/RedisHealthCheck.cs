using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace SmartDocs.Api.HealthChecks;

/// <summary>
/// Readiness probe for Redis (Ch 25) — the conversation-state and response-cache
/// hot store. Rather than open a raw socket, it exercises the same
/// <see cref="IDistributedCache"/> the app uses: a tiny set/get round-trip on a
/// reserved key. Reports <c>Degraded</c> when no Redis connection is configured
/// (the host falls back to an in-memory distributed cache, so a round-trip would
/// pass and falsely look "Healthy" — the configuration gate is what makes the
/// signal honest), <c>Unhealthy</c> when the round-trip throws.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private const string ProbeKey = "health:redis-probe";

    private readonly IDistributedCache _cache;
    private readonly DependencyEndpointOptions _options;

    /// <summary>Create the probe over the app's distributed cache.</summary>
    /// <param name="cache">The distributed cache (Redis in production).</param>
    /// <param name="options">The configured dependency endpoints.</param>
    public RedisHealthCheck(IDistributedCache cache, IOptions<DependencyEndpointOptions> options)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        _cache = cache;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.RedisConnection))
        {
            return HealthCheckResult.Degraded(
                "Redis connection is not configured; using the in-memory distributed cache.");
        }

        try
        {
            var token = Guid.NewGuid().ToByteArray();
            await _cache.SetAsync(
                ProbeKey,
                token,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) },
                cancellationToken).ConfigureAwait(false);

            var roundTripped = await _cache.GetAsync(ProbeKey, cancellationToken).ConfigureAwait(false);

            return roundTripped is not null && roundTripped.AsSpan().SequenceEqual(token)
                ? HealthCheckResult.Healthy("Redis round-trip succeeded.")
                : HealthCheckResult.Unhealthy("Redis round-trip did not return the written value.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Redis probe timed out.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The StackExchange.Redis connection surfaces its own exception types;
            // catching broadly (minus genuine cancellation) keeps a Redis outage
            // from throwing out of the health pipeline. The probe degrades the
            // readiness report instead. The filter satisfies CA1031.
            return HealthCheckResult.Unhealthy("Redis is unreachable.", ex);
        }
    }
}
