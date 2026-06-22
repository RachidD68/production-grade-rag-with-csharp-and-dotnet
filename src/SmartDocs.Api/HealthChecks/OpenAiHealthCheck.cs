using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace SmartDocs.Api.HealthChecks;

/// <summary>
/// Readiness probe for the Azure OpenAI endpoint (Ch 25). Issues an
/// unauthenticated GET against the resource root purely to confirm the service
/// answers — a 401/403 is treated as <c>Healthy</c> ("up, just wants a key"),
/// which keeps the probe from needing a credential. Reports <c>Degraded</c> when
/// no endpoint is configured (e.g. the Ollama dev profile), <c>Unhealthy</c> when
/// the configured endpoint is unreachable.
/// </summary>
public sealed class OpenAiHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DependencyEndpointOptions _options;

    /// <summary>Create the probe.</summary>
    /// <param name="httpClientFactory">Factory for the named short-timeout probe client.</param>
    /// <param name="options">The configured dependency endpoints.</param>
    public OpenAiHealthCheck(IHttpClientFactory httpClientFactory, IOptions<DependencyEndpointOptions> options)
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
        var endpoint = _options.OpenAiEndpoint;

        return HttpProbe.ProbeAsync(
            _httpClientFactory.CreateClient(SmartDocsHealthChecks.ProbeClientName),
            endpoint,
            endpoint ?? string.Empty,
            "Azure OpenAI",
            treatUnauthorizedAsReachable: true,
            cancellationToken);
    }
}
