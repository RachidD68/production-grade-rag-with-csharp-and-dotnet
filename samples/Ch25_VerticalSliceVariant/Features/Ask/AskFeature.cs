using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;
using SmartDocs.Generation;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.VectorStores;

namespace RagInDotNet.Samples.Ch25_VerticalSliceVariant.Features.Ask;

/// <summary>
/// Vertical-Slice "Ask" feature: request + response + handler + endpoint
/// mapper + DI registration all live in this single file. Compare with
/// SmartDocs.Api/Program.cs (horizontal layering across multiple projects).
/// </summary>
public static class AskFeature
{
    public sealed record AskRequest(string Question);
    public sealed record AskResponse(string Answer, IReadOnlyList<string> SourceChunkIds, long LatencyMs);

    public static void RegisterServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IVectorStore>(sp =>
        {
            var store = new InMemoryVectorStore("vsa-demo");
            var emb = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
            SeedAsync(store, emb).GetAwaiter().GetResult();
            return store;
        });
        services.AddSingleton<IRetriever>(sp =>
            new DenseRetriever(
                sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                sp.GetRequiredService<IVectorStore>()));
        services.AddSingleton(sp =>
            new PromptTemplateEngine(sp.GetRequiredService<ITokenCounter>()));
        services.AddSingleton<RagPipeline>();
    }

    public static void MapEndpoints(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapPost("/api/ask", async (AskRequest req, RagPipeline pipeline, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Question))
            {
                return Results.BadRequest(new { error = "Question is required." });
            }
            var resp = await pipeline.AskAsync(req.Question, ct);
            return Results.Ok(new AskResponse(
                resp.Answer,
                [.. resp.Sources.Select(s => s.Chunk.ChunkId)],
                resp.LatencyMs));
        }).WithName("Ask");
    }

    private static async Task SeedAsync(InMemoryVectorStore store, IEmbeddingGenerator<string, Embedding<float>> embeddings)
    {
        string[] snippets =
        [
            "Employees at the Montreal office receive 20 paid vacation days per fiscal year.",
            "Sick leave is unlimited; please notify your manager within 24 hours.",
            "Remote work is allowed up to 3 days per week with manager approval.",
        ];
        var meta = new DocumentMetadata("vsa", "hr-policies", "HR", "Montreal", "Internal", "Policy",
            2026, "Author", new DateOnly(2026, 1, 1), "VSA Demo");
        var emb = await embeddings.GenerateAsync(snippets);
        var chunks = snippets.Select((t, i) =>
            new EmbeddedChunk(
                new DocumentChunk($"vsa#{i}", "vsa", i, t, 0, t.Length, meta),
                emb[i].Vector,
                "ollama")).ToArray();
        await store.UpsertAsync(chunks);
    }
}
