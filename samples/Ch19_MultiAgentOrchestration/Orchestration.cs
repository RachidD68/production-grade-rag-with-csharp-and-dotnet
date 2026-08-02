using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;

namespace RagInDotNet.Samples.Ch19_MultiAgentOrchestration;

/// <summary>
/// Builds and runs the Chapter-19 multi-agent research graph with MAF Workflows
/// 1.16.0: Researcher → Analyst → FactChecker → Writer, wired as a typed graph of
/// agents-as-executors. The four agents reuse the same specialist instructions as
/// <c>SmartDocs.Agents.MultiAgentWorkflow</c>; here the framework owns the topology
/// (an explicit graph) rather than an LLM manager.
/// </summary>
public static class Orchestration
{
    /// <summary>The ordered pipeline steps, with the friendly event phrase each emits.</summary>
    public static readonly IReadOnlyList<(string AgentName, string StepEvent)> Steps =
    [
        ("researcher", "researcher.searching"),
        ("analyst", "analyst.extracting"),
        ("fact-checker", "fact-checker.verifying"),
        ("writer", "writer.composing"),
    ];

    /// <summary>
    /// Creates the four specialist agents, reusing the specialist instructions from
    /// <c>SmartDocs.Agents.MultiAgentWorkflow</c>. Each agent's instructions begin
    /// with a <c>ROLE:</c> marker so the offline stub chat client can route
    /// deterministically (the Researcher step runs a real dense retrieval against the
    /// corpus and emits <c>[Source N]</c> quotes).
    /// </summary>
    public static IReadOnlyList<AIAgent> BuildAgents(IChatClient chat)
    {
        ArgumentNullException.ThrowIfNull(chat);

        var researcher = new ChatClientAgent(chat,
            name: "researcher",
            description: "Finds relevant source material.",
            instructions:
                "ROLE:researcher You retrieve evidence from the SmartDocs knowledge base " +
                "and output a bullet list of source quotes with [Source N] markers.");

        var analyst = new ChatClientAgent(chat,
            name: "analyst",
            description: "Synthesizes findings into a draft answer.",
            instructions:
                "ROLE:analyst Given the Researcher's quotes, write a draft answer with " +
                "[Source N] inline citations.");

        var factChecker = new ChatClientAgent(chat,
            name: "fact-checker",
            description: "Validates each claim against the sources.",
            instructions:
                "ROLE:fact-checker For each sentence in the draft, reply SUPPORTED, " +
                "PARTIALLY_SUPPORTED, or NOT_SUPPORTED with a one-line justification.");

        var writer = new ChatClientAgent(chat,
            name: "writer",
            description: "Polishes the final answer.",
            instructions:
                "ROLE:writer Polish the draft into a concise, well-formatted final answer. " +
                "Preserve all [Source N] citations.");

        return [researcher, analyst, factChecker, writer];
    }

    /// <summary>
    /// Wires the four agents into a fixed Researcher → Analyst → FactChecker → Writer
    /// graph. Each agent becomes an executor via
    /// <c>AIAgent.BindAsExecutor(emitEvents: true)</c>; edges are typed references to
    /// those bindings (no string ids). The Writer's output is the workflow's output.
    /// </summary>
    public static Workflow BuildWorkflow(IReadOnlyList<AIAgent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        if (agents.Count != 4)
        {
            throw new ArgumentException("Expected exactly four agents (researcher, analyst, fact-checker, writer).", nameof(agents));
        }

        var researcher = agents[0].BindAsExecutor(emitEvents: true);
        var analyst = agents[1].BindAsExecutor(emitEvents: true);
        var factChecker = agents[2].BindAsExecutor(emitEvents: true);
        var writer = agents[3].BindAsExecutor(emitEvents: true);

        var builder = new WorkflowBuilder(researcher);
        builder.AddEdge(researcher, analyst);
        builder.AddEdge(analyst, factChecker);
        builder.AddEdge(factChecker, writer);
        builder.WithOutputFrom(writer);

        return builder.Build();
    }

    /// <summary>
    /// Runs <paramref name="question"/> through the graph end-to-end, streaming the
    /// per-agent step events (<c>researcher.searching</c>, <c>analyst.extracting</c>,
    /// <c>fact-checker.verifying</c>, <c>writer.composing</c>) to
    /// <paramref name="onStep"/>. Returns the Writer's final answer.
    /// </summary>
    public static async Task<string> RunAsync(
        Workflow workflow,
        string question,
        Action<string, string>? onStep = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var messages = new List<ChatMessage> { new(ChatRole.User, question) };

        await using var run = await InProcessExecution
            .RunStreamingAsync(workflow, messages, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // Drive the turn: agents-as-executors invoke their agent on receipt of a
        // TurnToken, emitting AgentResponseUpdateEvents as they go.
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true)).ConfigureAwait(false);

        // Accumulate the streamed text per executor (each agent may stream in chunks).
        var accumulated = new Dictionary<string, string>(StringComparer.Ordinal);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var lastWriterText = string.Empty;

        await foreach (var evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            if (evt is not AgentResponseUpdateEvent update)
            {
                continue;
            }

            var executorId = update.ExecutorId;
            accumulated[executorId] = (accumulated.TryGetValue(executorId, out var prior) ? prior : string.Empty)
                + (update.Update.Text ?? string.Empty);

            var step = StepEventFor(executorId);
            if (step is not null && emitted.Add(step))
            {
                onStep?.Invoke(step, accumulated[executorId]);
            }

            if (executorId.StartsWith("writer", StringComparison.Ordinal))
            {
                lastWriterText = accumulated[executorId];
            }
        }

        return lastWriterText;
    }

    /// <summary>
    /// Maps a workflow executor id (e.g. <c>fact_checker_ab12…</c>) back to its
    /// friendly step phrase. Executor ids are the agent name (with non-identifier
    /// characters replaced) plus a unique suffix, so we match on the prefix.
    /// </summary>
    private static string? StepEventFor(string executorId)
    {
        foreach (var (agentName, stepEvent) in Steps)
        {
            var prefix = agentName.Replace('-', '_');
            if (executorId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return stepEvent;
            }
        }
        return null;
    }
}
