using System.Net;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SmartDocs.Api.HealthChecks;

namespace SmartDocs.IntegrationTests.Api;

/// <summary>
/// Offline-degradation tests for the Ch 25 readiness probes. None of Qdrant,
/// OpenAI, Redis, or Cosmos is reachable here, so each check must report
/// <see cref="HealthStatus.Degraded"/> (when unconfigured) — never throw.
/// </summary>
public sealed class HealthCheckTests
{
    private static readonly HealthCheckContext Context = new();

    private static IHttpClientFactory HttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(SmartDocsHealthChecks.ProbeClientName);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    private static IOptions<DependencyEndpointOptions> Options(DependencyEndpointOptions opts) =>
        Microsoft.Extensions.Options.Options.Create(opts);

    private static MemoryDistributedCache MemoryCache() =>
        new(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public async Task Qdrant_unconfigured_endpoint_degrades()
    {
        var check = new QdrantHealthCheck(HttpClientFactory(), Options(new DependencyEndpointOptions()));

        var result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task OpenAi_unconfigured_endpoint_degrades()
    {
        var check = new OpenAiHealthCheck(HttpClientFactory(), Options(new DependencyEndpointOptions()));

        var result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task Cosmos_unconfigured_endpoint_degrades()
    {
        var check = new CosmosHealthCheck(HttpClientFactory(), Options(new DependencyEndpointOptions()));

        var result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task Redis_unconfigured_connection_degrades()
    {
        var check = new RedisHealthCheck(MemoryCache(), Options(new DependencyEndpointOptions()));

        var result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task Redis_configured_connection_round_trips_the_cache()
    {
        // RedisConnection is set so the probe runs the real round-trip; the
        // in-memory distributed cache stands in for Redis here and the set/get
        // succeeds, so the probe reports Healthy.
        var check = new RedisHealthCheck(
            MemoryCache(),
            Options(new DependencyEndpointOptions { RedisConnection = "localhost:6379" }));

        var result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Qdrant_unreachable_configured_endpoint_is_unhealthy()
    {
        // A configured but dead endpoint: the probe maps the connection failure to
        // Unhealthy rather than throwing. Port 9 (discard) refuses fast.
        var check = new QdrantHealthCheck(
            HttpClientFactory(),
            Options(new DependencyEndpointOptions { QdrantEndpoint = "http://127.0.0.1:9/" }));

        var result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
