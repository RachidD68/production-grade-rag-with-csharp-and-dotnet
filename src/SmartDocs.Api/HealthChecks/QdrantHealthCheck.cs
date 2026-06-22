using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace SmartDocs.Api.HealthChecks;

/// <summary>
/// Readiness probe for the Qdrant vector store (Ch 25). Hits Qdrant's
/// <c>/healthz</c> liveness endpoint. Reports <c>Healthy</c> when it answers 200,
/// <c>Unhealthy</c> when the configured endpoint is unreachable or erroring, and
/// <c>Degraded</c> when no Qdrant endpoint is configured (the in-memory store is
/// in use) — so the probe is safe to run offline.
/// </summary>
public sealed class QdrantHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DependencyEndpointOptions _options;

    /// <summary>Create the probe.</summary>
    /// <param name="httpClientFactory">Factory for the named short-timeout probe client.</param>
    /// <param name="options">The configured dependency endpoints.</param>
    public QdrantHealthCheck(IHttpClientFactory httpClientFactory, IOptions<DependencyEndpointOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var endpoint = _options.QdrantEndpoint;
        var probeUrl = string.IsNullOrWhiteSpace(endpoint)
            ? string.Empty
            : new Uri(new Uri(endpoint, UriKind.Absolute), "healthz").ToString();

        return HttpProbe.ProbeAsync(
            _httpClientFactory.CreateClient(SmartDocsHealthChecks.ProbeClientName),
            endpoint,
            probeUrl,
            "Qdrant",
            treatUnauthorizedAsReachable: false,
            cancellationToken);
    }
}
