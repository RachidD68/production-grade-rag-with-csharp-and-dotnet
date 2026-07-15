// Chapter 1 — The AI Landscape — Hello World RAG.
//
// 80 lines that take you from zero to a working RAG. We embed five
// hardcoded HR policy snippets into an in-memory list, ask
// "How many vacation days do I get?", retrieve the closest three by
// cosine similarity, and ask the LLM to answer using only that context.
//
// Everything in this sample (provider toggle, models, endpoint) flows
// from appsettings.json -> SmartDocs.Core.AddSmartDocsCore() -> DI.
// Default: Ollama at http://localhost:11434 with `llama3.2` + `nomic-embed-text`.
// Set SmartDocs__Llm__Provider=AzureOpenAI plus the matching values to swap.
//
// Run:
//   dotnet run --project samples/Ch01_HelloWorldRag

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RagInDotNet.Samples.Ch01_HelloWorldRag;
using SmartDocs.Core.DependencyInjection;

// Pin ContentRoot to the binary directory so `dotnet run --project ...`
// finds appsettings.json regardless of the caller's CWD.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Configuration.AddJsonFile("appsettings.json", optional: false);

// The Challenge exercise's no-result threshold: when the best match is too
// weak, skip the LLM call and return a grounded "not enough information".
// Absent or 0 preserves the original always-answer behavior.
var minScore = double.TryParse(
    builder.Configuration["SmartDocs:Llm:NoResultThreshold"],
    System.Globalization.CultureInfo.InvariantCulture,
    out var threshold)
    ? threshold
    : 0.0;

builder.Services.AddSmartDocsCore(builder.Configuration);
using var host = builder.Build();

var embeddings = host.Services.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
var chat = host.Services.GetRequiredService<IChatClient>();

var answer = await HelloWorldRag.AskAsync(
    embeddings,
    chat,
    HelloWorldRag.HardcodedHrPolicies,
    "How many vacation days do I get?",
    minScore: minScore);

Console.WriteLine();
Console.WriteLine("=== Answer ===");
Console.WriteLine(answer);
