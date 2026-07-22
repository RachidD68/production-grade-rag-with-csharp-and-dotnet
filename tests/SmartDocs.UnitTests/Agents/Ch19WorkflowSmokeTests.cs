using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using RagInDotNet.Samples.Ch19_MultiAgentOrchestration;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.VectorStores;

namespace SmartDocs.UnitTests.Agents;

/// <summary>
/// Smoke test for the Chapter-19 multi-agent orchestration sample: the
/// <see cref="Orchestration"/> graph builds with the real MAF Workflows edge
/// API and runs one query end-to-end against the offline corpus, emitting the four
/// agent-step events in order. Fully offline (FNV embedder + in-memory store +
/// deterministic stub chat client).
/// </summary>
public sealed class Ch19WorkflowSmokeTests
{
    [Fact]
    public void Workflow_builds_with_the_four_agent_pipeline_shape()
    {
        var (_, retriever) = BuildOfflineStack();
        var chat = new RagInDotNet.Samples.Ch19_MultiAgentOrchestration.StubChatClient(retriever);
        var agents = Orchestration.BuildAgents(chat);
        var workflow = Orchestration.BuildWorkflow(agents);

        // Graph-shape assertion: exactly four executors, starting at the researcher.
        var executors = workflow.ReflectExecutors().Keys.ToList();
        Assert.Equal(4, executors.Count);
        Assert.StartsWith("researcher", workflow.StartExecutorId, StringComparison.Ordinal);
        Assert.Contains(executors, e => e.StartsWith("analyst", StringComparison.Ordinal));
        Assert.Contains(executors, e => e.StartsWith("fact_checker", StringComparison.Ordinal));
        Assert.Contains(executors, e => e.StartsWith("writer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Workflow_runs_one_query_end_to_end_and_emits_the_four_step_events()
    {
        var (_, retriever) = BuildOfflineStack();
        var chat = new RagInDotNet.Samples.Ch19_MultiAgentOrchestration.StubChatClient(retriever);
        var agents = Orchestration.BuildAgents(chat);
        var workflow = Orchestration.BuildWorkflow(agents);

        var steps = new List<string>();
        var answer = await Orchestration.RunAsync(
            workflow,
            "How many days a week can I work from home?",
            onStep: (step, _) => steps.Add(step));

        Assert.Equal(
            ["researcher.searching", "analyst.extracting", "fact-checker.verifying", "writer.composing"],
            steps);
        Assert.False(string.IsNullOrWhiteSpace(answer));
        Assert.Contains("[Source 1]", answer, StringComparison.Ordinal);
    }

    private static (InMemoryVectorStore Store, DenseRetriever Retriever) BuildOfflineStack()
    {
        var chunks = Corpus.Build();
        IEmbeddingGenerator<string, Embedding<float>> embedder = new BagOfWordsEmbeddingGenerator();
        var embeddingService = new EmbeddingService(
            embedder, "bag-of-words-256", 256, NullLogger<EmbeddingService>.Instance);

        var store = new InMemoryVectorStore("ch19-smoke");
        store.EnsureCollectionExistsAsync().GetAwaiter().GetResult();
        foreach (var chunk in chunks)
        {
            var embedded = embeddingService.EmbedAsync(chunk).GetAwaiter().GetResult();
            store.UpsertAsync([embedded]).GetAwaiter().GetResult();
        }
        return (store, new DenseRetriever(embeddingService, store));
    }
}
