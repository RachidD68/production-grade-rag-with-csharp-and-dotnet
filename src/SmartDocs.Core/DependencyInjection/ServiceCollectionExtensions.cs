using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;
using SmartDocs.Core.Configuration;
using SmartDocs.Core.Tokens;

namespace SmartDocs.Core.DependencyInjection;

/// <summary>
/// DI registration extensions for SmartDocs.Core. The seed of the
/// <c>services.AddSmartDocsRagPipeline()</c> NuGet pattern that ships in
/// Chapter 25.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wire up the SmartDocs core services: <see cref="LlmClientOptions"/>,
    /// <see cref="IChatClient"/>, <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>,
    /// and <see cref="ITokenCounter"/>.
    /// </summary>
    /// <param name="services">The DI container being built.</param>
    /// <param name="configuration">
    /// Application configuration. The <c>SmartDocs:Llm</c> section is bound
    /// to <see cref="LlmClientOptions"/>; the active provider determines
    /// which client implementation is registered.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddSmartDocsCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<LlmClientOptions>()
            .Bind(configuration.GetSection(LlmClientOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                static opts => opts.Provider != LlmProvider.AzureOpenAI || !string.IsNullOrWhiteSpace(opts.ApiKey),
                "SmartDocs:Llm:ApiKey is required when Provider is AzureOpenAI.")
            .ValidateOnStart();

        services.AddSingleton<IChatClient>(BuildChatClient);
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(BuildEmbeddingGenerator);
        services.AddSingleton<ITokenCounter>(_ => new TokenCounter());

        return services;
    }

    private static IChatClient BuildChatClient(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<LlmClientOptions>>().Value;
        return options.Provider switch
        {
            LlmProvider.Ollama => new OllamaApiClient(new Uri(options.Endpoint), options.ChatModel),

            LlmProvider.AzureOpenAI => CreateAzureOpenAIClient(options)
                .GetChatClient(options.ChatModel)
                .AsIChatClient(),

            _ => throw new InvalidOperationException($"Unsupported provider: {options.Provider}"),
        };
    }

    private static IEmbeddingGenerator<string, Embedding<float>> BuildEmbeddingGenerator(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptions<LlmClientOptions>>().Value;
        return options.Provider switch
        {
            LlmProvider.Ollama => new OllamaApiClient(new Uri(options.Endpoint), options.EmbeddingModel),

            LlmProvider.AzureOpenAI => CreateAzureOpenAIClient(options)
                .GetEmbeddingClient(options.EmbeddingModel)
                .AsIEmbeddingGenerator(),

            _ => throw new InvalidOperationException($"Unsupported provider: {options.Provider}"),
        };
    }

    private static AzureOpenAIClient CreateAzureOpenAIClient(LlmClientOptions options)
    {
        var apiKey = options.ApiKey
            ?? throw new InvalidOperationException("SmartDocs:Llm:ApiKey is required when Provider is AzureOpenAI.");
        return new AzureOpenAIClient(new Uri(options.Endpoint), new ApiKeyCredential(apiKey));
    }
}
