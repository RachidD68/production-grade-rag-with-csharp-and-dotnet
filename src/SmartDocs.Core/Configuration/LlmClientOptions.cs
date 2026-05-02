using System.ComponentModel.DataAnnotations;

namespace SmartDocs.Core.Configuration;

/// <summary>The set of LLM providers SmartDocs is wired to talk to.</summary>
public enum LlmProvider
{
    /// <summary>
    /// Local Ollama via <c>OllamaSharp.OllamaApiClient</c>. Default for
    /// development; works offline and incurs no API cost.
    /// </summary>
    Ollama,

    /// <summary>
    /// Azure OpenAI via <c>Azure.AI.OpenAI</c> +
    /// <c>Microsoft.Extensions.AI.OpenAI</c>. The default production target
    /// throughout the book.
    /// </summary>
    AzureOpenAI,
}

/// <summary>
/// Configuration for the chat and embedding clients SmartDocs registers
/// in DI. Bound from the <c>SmartDocs:Llm</c> section of
/// <c>appsettings.json</c> by
/// <see cref="DependencyInjection.ServiceCollectionExtensions.AddSmartDocsCore"/>.
/// </summary>
public sealed class LlmClientOptions
{
    /// <summary>Configuration section path bound to this class.</summary>
    public const string SectionName = "SmartDocs:Llm";

    /// <summary>Which provider to wire up.</summary>
    public LlmProvider Provider { get; set; } = LlmProvider.Ollama;

    /// <summary>
    /// Provider endpoint. For Ollama: <c>http://localhost:11434</c>. For
    /// Azure OpenAI: the resource endpoint URL
    /// (e.g. <c>https://my-aoai.openai.azure.com/</c>).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Required when <see cref="Provider"/> is <see cref="LlmProvider.AzureOpenAI"/>;
    /// ignored otherwise. Read from the matching environment variable or
    /// user-secrets store, never committed to source control.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Chat model identifier. Ollama: a tag like <c>llama3.2</c>. Azure OpenAI:
    /// the deployment name (NOT the upstream model name).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ChatModel { get; set; } = "llama3.2";

    /// <summary>
    /// Embedding model identifier. Ollama: <c>nomic-embed-text</c>.
    /// Azure OpenAI: the deployment name for an embedding model
    /// (e.g. <c>text-embedding-3-small</c>).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
}
