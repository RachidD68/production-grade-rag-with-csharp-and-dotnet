using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartDocs.Api.HealthChecks;

/// <summary>
/// Shared offline-safe HTTP reachability probe for the dependency readiness
/// checks (Ch 25). The contract every check shares: a <em>missing</em> endpoint
/// is <c>Degraded</c> (the dependency simply is not configured in this
/// environment — not a failure), a <em>reachable</em> endpoint is <c>Healthy</c>,
/// and an endpoint that is configured but unreachable / erroring is
/// <c>Unhealthy</c>. Crucially, a network exception is caught and mapped — never
/// rethrown — so a probe against a down dependency degrades the readiness report
/// instead of throwing out of the health pipeline.
/// </summary>
internal static class HttpProbe
{
    /// <summary>
    /// GET <paramref name="probeUrl"/> and map the outcome to a health result.
    /// </summary>
    /// <param name="httpClient">The client to probe with (a short timeout is expected from the caller).</param>
    /// <param name="endpoint">The configured base endpoint, or null/blank when unconfigured.</param>
    /// <param name="probeUrl">The absolute URL to GET.</param>
    /// <param name="dependencyName">Human name used in the result description.</param>
    /// <param name="treatUnauthorizedAsReachable">
    /// When true, a 401/403 still counts as <c>Healthy</c> — the endpoint answered,
    /// it just wants credentials (the right signal for "is the service up?" probes
    /// against Azure OpenAI / Cosmos where the probe is intentionally unauthenticated).
    /// </param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    public static async Task<HealthCheckResult> ProbeAsync(
        HttpClient httpClient,
        string? endpoint,
        string probeUrl,
        string dependencyName,
        bool treatUnauthorizedAsReachable,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return HealthCheckResult.Degraded($"{dependencyName} endpoint is not configured.");
        }

        try
        {
            using var response = await httpClient
                .GetAsync(new Uri(probeUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy($"{dependencyName} is reachable.");
            }

            if (treatUnauthorizedAsReachable &&
                (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                 response.StatusCode == System.Net.HttpStatusCode.Forbidden))
            {
                return HealthCheckResult.Healthy(
                    $"{dependencyName} answered ({(int)response.StatusCode}) — reachable, auth not probed.");
            }

            return HealthCheckResult.Unhealthy(
                $"{dependencyName} returned {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The probe's own timeout fired (not the host cancelling): the endpoint
            // is configured but did not answer in time — unhealthy, not a throw.
            return HealthCheckResult.Unhealthy($"{dependencyName} timed out.");
        }
        catch (HttpRequestException ex)
        {
            return HealthCheckResult.Unhealthy($"{dependencyName} is unreachable.", ex);
        }
    }
}
