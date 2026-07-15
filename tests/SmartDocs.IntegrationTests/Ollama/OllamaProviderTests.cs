using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartDocs.Core.DependencyInjection;

namespace SmartDocs.IntegrationTests.Ollama;

/// <summary>
/// Real Ollama-backed integration tests for the Phase 1 LLM wiring. These
/// tests are gated behind the <c>RUN_OLLAMA_INTEGRATION</c> environment
/// variable so they don't fail in CI environments without an Ollama daemon.
/// Install Ollama natively and pull the models (see docs/local-setup.md), then:
///
/// <code>
///   ollama pull nomic-embed-text
///   ollama pull llama3.2
///   $env:RUN_OLLAMA_INTEGRATION="1"  # PowerShell
///   # export RUN_OLLAMA_INTEGRATION=1  # bash
///   dotnet test --filter "FullyQualifiedName~Ollama"
/// </code>
/// </summary>
[Trait("Category", "RealOllama")]
public sealed class OllamaProviderTests
{
    private const string GateVar = "RUN_OLLAMA_INTEGRATION";
    private const string GateMessage = "Set RUN_OLLAMA_INTEGRATION=1 and start a local Ollama to run.";

    [Fact]
    public async Task Ollama_provider_can_actually_embed_text()
    {
        if (!IsGateOpen(out var skipReason))
        {
            // xunit v2 has no runtime Assert.Skip; we vacuously pass with a
            // log line so opt-in test runs surface a clear "ran" / "skipped"
            // signal, and CI without Ollama doesn't fail.
            Console.WriteLine($"[SKIP] {skipReason}");
            return;
        }

        var sp = BuildOllamaProvider();
        var embeddings = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        var generated = await embeddings.GenerateAsync(["How many vacation days do I get?"]);

        Assert.NotNull(generated);
        Assert.Single(generated);
        var vector = generated[0].Vector;
        Assert.True(vector.Length > 0, "embedding vector should be non-empty");
        // nomic-embed-text is 768-dimensional.
        Assert.Equal(768, vector.Length);
    }

    [Fact]
    public async Task Ollama_provider_can_actually_chat()
    {
        if (!IsGateOpen(out var skipReason))
        {
            Console.WriteLine($"[SKIP] {skipReason}");
            return;
        }

        var sp = BuildOllamaProvider();
        var chat = sp.GetRequiredService<IChatClient>();

        var response = await chat.GetResponseAsync(
            "Reply in exactly five words. What is two plus two?");

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.Text), "chat response should not be empty");
    }

    private static bool IsGateOpen(out string skipReason)
    {
        if (Environment.GetEnvironmentVariable(GateVar) != "1")
        {
            skipReason = GateMessage;
            return false;
        }
        skipReason = string.Empty;
        return true;
    }

    private static ServiceProvider BuildOllamaProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SmartDocs:Llm:Provider"] = "Ollama",
                ["SmartDocs:Llm:Endpoint"] = "http://localhost:11434",
                ["SmartDocs:Llm:ChatModel"] = "llama3.2",
                ["SmartDocs:Llm:EmbeddingModel"] = "nomic-embed-text",
            })
            .Build();
        return new ServiceCollection()
            .AddSmartDocsCore(config)
            .BuildServiceProvider();
    }
}
