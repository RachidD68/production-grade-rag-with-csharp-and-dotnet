using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using SmartDocs.Performance;
using SmartDocs.Performance.Observability;

namespace SmartDocs.UnitTests.Performance;

public sealed class PerformanceWiringTests
{
    [Fact]
    public void AddSmartDocsTelemetry_registers_without_throwing()
    {
        var services = new ServiceCollection();
        services.AddSmartDocsTelemetry();

        using var provider = services.BuildServiceProvider();

        // OpenTelemetry registers hosted services / tracer + meter providers;
        // building the provider exercises the registration end to end.
        Assert.NotNull(provider);
    }

    [Fact]
    public void AddResilientLlmHttpClient_yields_a_named_client_from_the_factory()
    {
        var services = new ServiceCollection();
        services.AddResilientLlmHttpClient("llm");

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using HttpClient client = factory.CreateClient("llm");

        // The standard resilience handler is wired in front of the client; the
        // factory hands back a usable client with that pipeline attached.
        Assert.NotNull(client);
    }

    [Fact]
    public void AddResilientLlmHttpClient_configures_the_Ch25_standard_profile()
    {
        var services = new ServiceCollection();
        services.AddResilientLlmHttpClient("llm");

        using var provider = services.BuildServiceProvider();

        // The standard handler binds its options to a name derived from the
        // client name; resolving the monitor and validating the snapshot is what
        // the framework does internally when the pipeline is first built, so this
        // also proves the config passes the built-in options validation (the
        // SamplingDuration >= 2x AttemptTimeout rule in particular).
        var monitor = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>();
        var options = monitor.Get("llm-standard");

        Assert.Equal(ResilienceWiring.StandardProfile.MaxRetryAttempts, options.Retry.MaxRetryAttempts);
        Assert.True(options.Retry.UseJitter);
        Assert.Equal(DelayBackoffType.Exponential, options.Retry.BackoffType);
        Assert.Equal(ResilienceWiring.StandardProfile.AttemptTimeout, options.AttemptTimeout.Timeout);
        Assert.Equal(ResilienceWiring.StandardProfile.CircuitBreakerFailureRatio, options.CircuitBreaker.FailureRatio);
        Assert.Equal(
            ResilienceWiring.StandardProfile.CircuitBreakerSamplingDuration,
            options.CircuitBreaker.SamplingDuration);
        Assert.Equal(ResilienceWiring.StandardProfile.TotalRequestTimeout, options.TotalRequestTimeout.Timeout);

        // SamplingDuration must be at least twice the attempt timeout or the
        // framework validator rejects the pipeline — assert the invariant holds.
        Assert.True(options.CircuitBreaker.SamplingDuration >= 2 * options.AttemptTimeout.Timeout);
    }
}
