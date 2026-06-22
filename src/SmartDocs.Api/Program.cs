// SmartDocs.Api — Phase 2 (Ch 10).
//
// /health             diagnostic; reports active provider + models
// /api/ask            POST { question }; one-shot grounded answer + citations
// /api/ask/stream     POST { question }; SSE stream of {sources, token*, done}
//                     using ASP.NET Core 10's first-class TypedResults.ServerSentEvents
//
// For Phase 2 we seed an in-memory corpus with the Ch 1 HR snippets so
// the API has something to retrieve from out of the box. Phase 7 (Ch 25
// capstone) wires real ingestion against Qdrant + Neo4j.

using System.Net.ServerSentEvents;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartDocs.Api;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Configuration;
using SmartDocs.Core.DependencyInjection;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;
using SmartDocs.Generation;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.VectorStores;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSmartDocsCore(builder.Configuration);

builder.Services.AddSingleton<IVectorStore>(sp =>
{
    var store = new InMemoryVectorStore("smartdocs-api-demo");
    var embeddings = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
    SeedAsync(store, embeddings).GetAwaiter().GetResult();
    return store;
});
// The retriever embeds the query through IEmbeddingService so it picks up the
// model's query task prefix (the matching half of the document prefix used at
// index time). EmbeddingPrompt.None keeps OpenAI/Azure behaviour unchanged; an
// Ollama deployment would pass EmbeddingPrompt.Nomic/Mxbai here.
builder.Services.AddSingleton<IEmbeddingService>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<LlmClientOptions>>().Value;
    // Dimensions is metadata the dense retrieval path never reads; the real
    // width comes from the generated vectors. 1 satisfies the positive guard.
    return new EmbeddingService(
        sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
        opts.EmbeddingModel,
        dimensions: 1,
        sp.GetRequiredService<ILogger<EmbeddingService>>());
});
builder.Services.AddSingleton<IRetriever>(sp =>
    new DenseRetriever(
        sp.GetRequiredService<IEmbeddingService>(),
        sp.GetRequiredService<IVectorStore>()));
builder.Services.AddSingleton(sp =>
    new PromptTemplateEngine(sp.GetRequiredService<ITokenCounter>()));
builder.Services.AddSingleton<RagPipeline>();
// The IRagPipeline seam (Ch 21): callers depend on the interface so a decorator
// (e.g. ResponseCache) can wrap the pipeline transparently. Resolves to the same
// singleton RagPipeline instance registered above.
builder.Services.AddSingleton<IRagPipeline>(sp => sp.GetRequiredService<RagPipeline>());

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

app.MapPost("/api/ask", async (AskRequest req, RagPipeline pipeline, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Question))
    {
        return Results.BadRequest(new { error = "Question is required." });
    }
    var response = await pipeline.AskAsync(req.Question, ct);
    return Results.Ok(new AskResponse(
        Answer: response.Answer,
        Citations: response.Sources.Select((s, i) => new Citation(
            Index: i + 1,
            ChunkId: s.Chunk.ChunkId,
            DocumentId: s.Chunk.DocumentId,
            Title: s.Chunk.Metadata.Title,
            Score: s.Score)).ToArray(),
        LatencyMs: response.LatencyMs,
        Strategy: response.Strategy));
})
.WithName("Ask")
.WithTags("rag");

app.MapPost("/api/ask/stream", IResult (AskRequest req, RagPipeline pipeline, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Question))
    {
        return Results.BadRequest(new { error = "Question is required." });
    }
    return TypedResults.ServerSentEvents(StreamSseAsync(pipeline, req.Question, ct));
})
.WithName("AskStreaming")
.WithTags("rag");

app.Run();

static async IAsyncEnumerable<SseItem<string>> StreamSseAsync(
    RagPipeline pipeline,
    string question,
    // ASP.NET Core binds this CancellationToken to HttpContext.RequestAborted, so
    // when the browser closes the SSE connection mid-stream the token trips,
    // AskStreamingAsync's ThrowIfCancellationRequested fires, and the upstream LLM
    // call is abandoned — we stop paying for tokens nobody will read (Ch 21 §3.4).
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
{
    await foreach (var ev in pipeline.AskStreamingAsync(question, cancellationToken))
    {
        yield return ev.Kind switch
        {
            RagStreamEventKind.Sources => new SseItem<string>(
                System.Text.Json.JsonSerializer.Serialize(ev.Sources?.Select(s => s.Chunk.ChunkId).ToArray() ?? []),
                "sources"),
            RagStreamEventKind.Token => new SseItem<string>(ev.Token ?? "", "token"),
            RagStreamEventKind.Error => new SseItem<string>(ev.Token ?? "", "error"),
            RagStreamEventKind.Done => new SseItem<string>("", "done"),
            _ => new SseItem<string>("", "unknown"),
        };
    }
}

static async Task SeedAsync(InMemoryVectorStore store, IEmbeddingGenerator<string, Embedding<float>> embeddings)
{
    string[] hrSnippets =
    [
        "Employees at the Montreal office receive 20 paid vacation days per fiscal year, accrued monthly.",
        "Sick leave is unlimited for employees in good standing; please notify your manager within 24 hours.",
        "Remote work is allowed up to 3 days per week with prior manager approval.",
        "Annual performance reviews occur in March; salary adjustments take effect on May 1.",
        "Parental leave provides 18 weeks of fully paid time off, available to all primary and secondary caregivers.",
    ];
    var meta = new DocumentMetadata("hr-001", "hr-policies", "HR", "Montreal", "Internal", "Policy",
        2026, "Author", new DateOnly(2026, 1, 1), "HR Policy Snippets");
    var emb = await embeddings.GenerateAsync(hrSnippets);
    var chunks = new List<EmbeddedChunk>();
    for (int i = 0; i < hrSnippets.Length; i++)
    {
        var chunk = new DocumentChunk($"hr-001#{i}", "hr-001", i, hrSnippets[i], 0, hrSnippets[i].Length, meta);
        chunks.Add(new EmbeddedChunk(chunk, emb[i].Vector, "ollama"));
    }
    await store.UpsertAsync(chunks);
}

namespace SmartDocs.Api
{
    public sealed record HealthResponse(
        string Status, string Provider, string ChatModel, string EmbeddingModel,
        string Endpoint, string TokenCounterEncoding, string ChatClientType, string EmbeddingGeneratorType);

    public sealed record AskRequest(string Question);
    public sealed record Citation(int Index, string ChunkId, string DocumentId, string Title, double Score);
    public sealed record AskResponse(string Answer, IReadOnlyList<Citation> Citations, long LatencyMs, string Strategy);

    public partial class Program;
}
