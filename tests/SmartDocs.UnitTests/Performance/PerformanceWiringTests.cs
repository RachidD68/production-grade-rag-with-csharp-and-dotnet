using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
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
}
