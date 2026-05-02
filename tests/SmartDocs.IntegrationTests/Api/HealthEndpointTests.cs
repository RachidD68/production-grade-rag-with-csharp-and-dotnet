using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartDocs.Api;

namespace SmartDocs.IntegrationTests.Api;

/// <summary>
/// In-memory smoke test that boots SmartDocs.Api end-to-end via
/// <see cref="WebApplicationFactory{TEntryPoint}"/> and asserts the
/// AddSmartDocsCore() wiring produced sane DI. No external services
/// (Ollama, Azure OpenAI) are contacted — only construction is exercised.
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_endpoint_reports_active_provider_and_models()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);

        // appsettings.json defaults — verify the binding made it through.
        Assert.Equal("Ollama", body.Provider);
        Assert.Equal("llama3.2", body.ChatModel);
        Assert.Equal("nomic-embed-text", body.EmbeddingModel);
        Assert.Equal("http://localhost:11434", body.Endpoint);
        Assert.Equal("cl100k_base", body.TokenCounterEncoding);

        // Sanity: the registered concrete types should be the ones we expect
        // for the Ollama provider.
        Assert.Contains("OllamaApiClient", body.ChatClientType, StringComparison.Ordinal);
        Assert.Contains("OllamaApiClient", body.EmbeddingGeneratorType, StringComparison.Ordinal);
    }
}
