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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartDocs.Api;
using SmartDocs.Api.HealthChecks;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Configuration;
using SmartDocs.Core.DependencyInjection;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;
using SmartDocs.Generation;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Operations.Budgeting;
using SmartDocs.Performance;
using SmartDocs.Performance.Conversations;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.VectorStores;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSmartDocsCore(builder.Configuration);

// --- Ch 25 production hardening -------------------------------------------------

// 3.1 Resilience: register the resilient named HttpClient (4 retries + jitter,
// 30 s attempt timeout, 50% circuit breaker, 100 s total budget) for the Azure
// OpenAI + embedding transports. The provider SDK / IChatClient is handed this
// client in a real deployment; here it makes the pipeline available to DI.
builder.Services.AddResilientLlmHttpClient("llm");
builder.Services.AddResilientLlmHttpClient("embeddings");

// IDistributedCache for the response cache, conversation hot store, and the Redis
// readiness probe. Redis when configured (SmartDocs:Dependencies:RedisConnection),
// else the in-memory distributed cache so the dev inner loop and tests still work.
var redisConnection = builder.Configuration[$"{DependencyEndpointOptions.SectionName}:RedisConnection"];
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnection);
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

// 3.5 Conversation state: Redis-backed hot store when Redis is configured, else
// the process-local store. Cosmos is the durable tier behind the same seam.
if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddDistributedConversationState();
}
else
{
    builder.Services.AddInMemoryConversationState();
}

// 3.7 Feature flags / runtime config: the configuration-backed gate. Backed by
// Azure App Configuration + feature management in a managed deployment.
builder.Services.AddFeatureGate();

// 3.3 Health checks: a `self` liveness check (tag `live`) plus the Qdrant /
// OpenAI / Redis / Cosmos readiness probes (tag `ready`). All degrade gracefully
// offline rather than throwing.
builder.Services.AddSmartDocsHealthChecks(builder.Configuration);

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

// 3.6 Budget enforcement: when the flag is on, decorate the IRagPipeline with the
// per-tenant governor (spend quota + rate limit + spend circuit-breaker) so a
// runaway/abusive tenant is BLOCKED — not merely alerted — before any token is
// spent. The decorator becomes the outermost IRagPipeline layer. Gated by the
// composition-time feature flag Features:budget.enforcement.enabled.
if (builder.Configuration.GetValue($"{ConfigurationFeatureGate.SectionName}:budget.enforcement.enabled", false))
{
    builder.Services.AddBudgetEnforcement();
}

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

// 3.3 Liveness vs readiness (Ch 25). /health/live runs only the `self` check —
// "the process is up" — so a dependency outage never restarts a healthy pod.
// /health/ready runs the dependency probes — "can this instance serve traffic" —
// which the load balancer / ingress uses to gate routing. The Bicep probe targets
// /health/ready. The existing /health stays as the human diagnostic view.
app.MapHealthChecks("/health/live", new()
{
    Predicate = check => check.Tags.Contains(SmartDocsHealthChecks.LiveTag),
})
.WithTags("diagnostics");

app.MapHealthChecks("/health/ready", new()
{
    Predicate = check => check.Tags.Contains(SmartDocsHealthChecks.ReadyTag),
})
.WithTags("diagnostics");

app.MapPost("/api/ask", async (AskRequest req, IRagPipeline pipeline, IFeatureGate features, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Question))
    {
        return Results.BadRequest(new { error = "Question is required." });
    }

    // 3.7 Real request-time feature-gate consumer: when graph retrieval is flagged
    // on at runtime, the answer is tagged so a caller can observe the active route.
    // The flag is read per request, so flipping it in configuration (Azure App
    // Configuration in production) changes behaviour with no redeploy.
    var graphEnabled = features.IsEnabled("graph-retrieval.enabled");

    RagResponse response;
    try
    {
        response = await pipeline.AskAsync(req.Question, ct);
    }
    catch (BudgetExceededException)
    {
        // 3.6 The governor blocked this tenant — surface a 429, not a 500.
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    return Results.Ok(new AskResponse(
        Answer: response.Answer,
        Citations: response.Sources.Select((s, i) => new Citation(
            Index: i + 1,
            ChunkId: s.Chunk.ChunkId,
            DocumentId: s.Chunk.DocumentId,
            Title: s.Chunk.Metadata.Title,
            Score: s.Score)).ToArray(),
        LatencyMs: response.LatencyMs,
        Strategy: graphEnabled ? response.Strategy + "+graph" : response.Strategy));
})
.WithName("Ask")
.WithTags("rag");

app.MapPost("/api/ask/stream", IResult (AskRequest req, IRagPipeline pipeline, CancellationToken ct) =>
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
    IRagPipeline pipeline,
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
