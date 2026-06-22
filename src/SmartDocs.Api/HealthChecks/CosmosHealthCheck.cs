using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace SmartDocs.Api.HealthChecks;

/// <summary>
/// Readiness probe for Azure Cosmos DB (Ch 25) — the durable store behind the
/// conversation-state seam. Hits the account endpoint with an unauthenticated GET;
/// a 401 is the expected "service is up, present a key" answer and counts as
/// <c>Healthy</c>. Reports <c>Degraded</c> when no Cosmos endpoint is configured,
/// <c>Unhealthy</c> when a configured endpoint is unreachable. Probing over plain
/// HTTP keeps the API package free of the <c>Microsoft.Azure.Cosmos</c> SDK.
/// </summary>
public sealed class CosmosHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DependencyEndpointOptions _options;

    /// <summary>Create the probe.</summary>
    /// <param name="httpClientFactory">Factory for the named short-timeout probe client.</param>
    /// <param name="options">The configured dependency endpoints.</param>
    public CosmosHealthCheck(IHttpClientFactory httpClientFactory, IOptions<DependencyEndpointOptions> options)
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
        var endpoint = _options.CosmosEndpoint;

        return HttpProbe.ProbeAsync(
            _httpClientFactory.CreateClient(SmartDocsHealthChecks.ProbeClientName),
            endpoint,
            endpoint ?? string.Empty,
            "Cosmos DB",
            treatUnauthorizedAsReachable: true,
            cancellationToken);
    }
}
