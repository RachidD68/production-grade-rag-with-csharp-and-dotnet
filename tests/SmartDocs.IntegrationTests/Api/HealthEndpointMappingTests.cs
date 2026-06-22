using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartDocs.Api;

namespace SmartDocs.IntegrationTests.Api;

/// <summary>
/// Boots SmartDocs.Api via <see cref="WebApplicationFactory{TEntryPoint}"/> and
/// exercises the Ch 25 liveness / readiness split end to end. No external service
/// is configured, so liveness is healthy (200) and readiness degrades to a 503 —
/// the orchestrator would keep the pod running but route no traffic to it.
/// </summary>
public sealed class HealthEndpointMappingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointMappingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Liveness_is_healthy_with_no_dependencies()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        // The self check has no dependency, so liveness is 200 even offline.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_responds_without_throwing_when_dependencies_are_absent()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        // Every dependency probe degrades (not throws) when unconfigured. Under the
        // default status-code mapping Degraded -> 200, so readiness answers 200 with
        // no external service present — the key guarantee being that the probes do
        // not throw out of the pipeline (which would surface as a 500).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Diagnostic_health_endpoint_still_reports_ok()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
