// SmartDocs.Api — Phase 1
//
// DI is wired through SmartDocs.Core.AddSmartDocsCore(): the active provider
// (Ollama or AzureOpenAI) is bound from the SmartDocs:Llm config section,
// and IChatClient + IEmbeddingGenerator + ITokenCounter become available
// to every endpoint.
//
// Endpoints in Phase 1 are intentionally minimal:
//   GET /health   -> 200 OK with provider + model details (proves DI worked)
// The full /api/ask + /api/ask/stream surface lands in Phase 2 (Ch 10).

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using SmartDocs.Api;
using SmartDocs.Core.Configuration;
using SmartDocs.Core.DependencyInjection;
using SmartDocs.Core.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSmartDocsCore(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", (
    IOptions<LlmClientOptions> llmOptions,
    IChatClient chat,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ITokenCounter tokens) =>
{
    var opts = llmOptions.Value;
    return Results.Ok(new HealthResponse(
        Status: "ok",
        Provider: opts.Provider.ToString(),
        ChatModel: opts.ChatModel,
        EmbeddingModel: opts.EmbeddingModel,
        Endpoint: opts.Endpoint,
        TokenCounterEncoding: tokens.EncodingName,
        ChatClientType: chat.GetType().FullName ?? chat.GetType().Name,
        EmbeddingGeneratorType: embeddings.GetType().FullName ?? embeddings.GetType().Name));
})
.WithName("Health")
.WithTags("diagnostics");

app.Run();

namespace SmartDocs.Api
{
    /// <summary>Response shape for <c>GET /health</c>. Public so integration tests can deserialise it.</summary>
    public sealed record HealthResponse(
        string Status,
        string Provider,
        string ChatModel,
        string EmbeddingModel,
        string Endpoint,
        string TokenCounterEncoding,
        string ChatClientType,
        string EmbeddingGeneratorType);

    /// <summary>
    /// Marker type so integration tests can reference the API entrypoint via
    /// <c>WebApplicationFactory&lt;Program&gt;</c>.
    /// </summary>
    public partial class Program;
}
