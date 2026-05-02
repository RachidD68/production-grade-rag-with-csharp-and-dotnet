using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartDocs.Core.Configuration;
using SmartDocs.Core.DependencyInjection;
using SmartDocs.Core.Tokens;

namespace SmartDocs.UnitTests.Configuration;

public sealed class LlmClientOptionsTests
{
    [Fact]
    public void Ollama_provider_binds_with_default_values_when_section_is_empty()
    {
        var sp = BuildProvider(new Dictionary<string, string?>());

        var opts = sp.GetRequiredService<IOptions<LlmClientOptions>>().Value;

        Assert.Equal(LlmProvider.Ollama, opts.Provider);
        Assert.Equal("http://localhost:11434", opts.Endpoint);
        Assert.Equal("llama3.2", opts.ChatModel);
        Assert.Equal("nomic-embed-text", opts.EmbeddingModel);
    }

    [Fact]
    public void Ollama_provider_resolves_IChatClient_and_IEmbeddingGenerator()
    {
        var sp = BuildProvider(new Dictionary<string, string?>
        {
            ["SmartDocs:Llm:Provider"] = "Ollama",
            ["SmartDocs:Llm:Endpoint"] = "http://localhost:11434",
            ["SmartDocs:Llm:ChatModel"] = "llama3.2",
            ["SmartDocs:Llm:EmbeddingModel"] = "nomic-embed-text",
        });

        Assert.NotNull(sp.GetRequiredService<IChatClient>());
        Assert.NotNull(sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>());
        Assert.NotNull(sp.GetRequiredService<ITokenCounter>());
    }

    [Fact]
    public void AzureOpenAI_provider_resolves_with_api_key()
    {
        var sp = BuildProvider(new Dictionary<string, string?>
        {
            ["SmartDocs:Llm:Provider"] = "AzureOpenAI",
            ["SmartDocs:Llm:Endpoint"] = "https://my-aoai.openai.azure.com/",
            ["SmartDocs:Llm:ApiKey"] = "test-key-only",
            ["SmartDocs:Llm:ChatModel"] = "gpt-4o-mini-deployment",
            ["SmartDocs:Llm:EmbeddingModel"] = "text-embedding-3-small-deployment",
        });

        Assert.NotNull(sp.GetRequiredService<IChatClient>());
        Assert.NotNull(sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>());
    }

    [Fact]
    public void AzureOpenAI_provider_without_api_key_fails_validation()
    {
        var sp = BuildProvider(new Dictionary<string, string?>
        {
            ["SmartDocs:Llm:Provider"] = "AzureOpenAI",
            ["SmartDocs:Llm:Endpoint"] = "https://my-aoai.openai.azure.com/",
            // ApiKey deliberately missing
            ["SmartDocs:Llm:ChatModel"] = "gpt-4o-mini-deployment",
            ["SmartDocs:Llm:EmbeddingModel"] = "text-embedding-3-small-deployment",
        });

        var ex = Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<LlmClientOptions>>().Value);

        Assert.Contains("ApiKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_chat_model_fails_data_annotations_validation()
    {
        var sp = BuildProvider(new Dictionary<string, string?>
        {
            ["SmartDocs:Llm:Provider"] = "Ollama",
            ["SmartDocs:Llm:Endpoint"] = "http://localhost:11434",
            ["SmartDocs:Llm:ChatModel"] = "", // invalid
            ["SmartDocs:Llm:EmbeddingModel"] = "nomic-embed-text",
        });

        Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<LlmClientOptions>>().Value);
    }

    [Fact]
    public void Section_name_constant_matches_documented_path()
    {
        Assert.Equal("SmartDocs:Llm", LlmClientOptions.SectionName);
    }

    private static ServiceProvider BuildProvider(IDictionary<string, string?> config)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        services.AddSmartDocsCore(configuration);
        return services.BuildServiceProvider();
    }
}
