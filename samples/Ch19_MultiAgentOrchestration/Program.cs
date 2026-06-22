// Ch 19 — Multi-agent orchestration with the MAF Workflows graph API (1.10.0)
//
// A Researcher → Analyst → FactChecker → Writer graph, wired with the real
// WorkflowBuilder edge API and AI-agents-as-executors, run end-to-end against an
// offline ~150-chunk SmartDocs corpus. The four agents reuse the specialist
// instructions from SmartDocs.Agents.MultiAgentWorkflow; here the framework owns
// the topology (an explicit typed graph) instead of an LLM manager.
//
// The graph is built with:
//   - AIAgent.BindAsExecutor(emitEvents: true)         — agent → ExecutorBinding
//   - new WorkflowBuilder(start) + AddEdge(a, b)        — typed edge references
//   - WithOutputFrom(writer) + Build()                  — the Writer's output is the result
//   - InProcessExecution.RunStreamingAsync(workflow, …) — a StreamingRun
//   - run.WatchStreamAsync() yielding WorkflowEvents,
//     of which AgentResponseUpdateEvent carries .ExecutorId and .Update.Text
//
// As each agent first produces output the sample prints its step event
// (researcher.searching, analyst.extracting, fact-checker.verifying,
// writer.composing), then prints the Writer's final answer.
//
// Fully offline and deterministic: a bag-of-words FNV embedder + the in-memory
// vector store + a role-routing stub chat client. No model, no API key.
//
// Run: dotnet run --project samples/Ch19_MultiAgentOrchestration

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using RagInDotNet.Samples.Ch19_MultiAgentOrchestration;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.VectorStores;

Console.WriteLine("=== Ch19: Multi-agent orchestration — Researcher → Analyst → FactChecker → Writer ===");
Console.WriteLine();

// --- Offline corpus + dense retriever (FNV embedder + in-memory store). -------
var chunks = Corpus.Build();
IEmbeddingGenerator<string, Embedding<float>> embedder = new BagOfWordsEmbeddingGenerator();
var embeddingService = new EmbeddingService(
    embedder, "bag-of-words-256", 256, NullLogger<EmbeddingService>.Instance);

var store = new InMemoryVectorStore("ch19-orchestration");
await store.EnsureCollectionExistsAsync().ConfigureAwait(false);
foreach (var chunk in chunks)
{
    var embedded = await embeddingService.EmbedAsync(chunk).ConfigureAwait(false);
    await store.UpsertAsync([embedded]).ConfigureAwait(false);
}
var retriever = new DenseRetriever(embeddingService, store);

Console.WriteLine(
    $"Corpus: {chunks.Count} chunks across {chunks.Select(c => c.DocumentId).Distinct().Count()} documents.");
Console.WriteLine();

// --- Build the agents and the workflow graph. ---------------------------------
// The Researcher step retrieves from the corpus; the stub chat client holds the
// retriever so the offline run produces real [Source N] evidence.
var chat = new StubChatClient(retriever);
var agents = Orchestration.BuildAgents(chat);
var workflow = Orchestration.BuildWorkflow(agents);

Console.WriteLine($"Graph start executor: {workflow.StartExecutorId}");
Console.WriteLine($"Executors: {string.Join(" -> ", workflow.ReflectExecutors().Keys.Select(ShortId))}");
Console.WriteLine();

// --- Run one question end-to-end, printing each agent's step event. -----------
var question = args.Length > 0 ? string.Join(' ', args) : "How many days a week can I work from home?";
Console.WriteLine($"Question: {question}");
Console.WriteLine();
Console.WriteLine("Agent steps:");

var finalAnswer = await Orchestration.RunAsync(
    workflow,
    question,
    onStep: (step, text) => Console.WriteLine($"  [{step}] {Truncate(text)}"))
    .ConfigureAwait(false);

Console.WriteLine();
Console.WriteLine("Final answer:");
Console.WriteLine($"  {finalAnswer}");
return 0;

static string ShortId(string id)
{
    // Executor ids are "<agent-name>_<guid>"; drop the trailing 32-hex-char suffix.
    var lastUnderscore = id.LastIndexOf('_');
    return lastUnderscore > 0 && id.Length - lastUnderscore - 1 == 32
        ? id[..lastUnderscore]
        : id;
}

static string Truncate(string text)
{
    var line = text.ReplaceLineEndings(" ").Trim();
    return line.Length > 120 ? line[..120] + "…" : line;
}
