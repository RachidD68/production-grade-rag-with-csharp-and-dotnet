using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;

namespace SmartDocs.Agents;

/// <summary>
/// The SmartDocs multi-agent workflow from Ch 19.
///
/// Ships two orchestration patterns side-by-side:
///
/// <list type="bullet">
///   <item><b>Agents-as-tools (LLM-as-manager)</b> — a Manager agent receives
///   the question and drives the workers via tool calls. The Manager decides at
///   each turn which worker to invoke (Researcher, Analyst, FactChecker,
///   Writer), passes a directive, and consumes the worker's reply. Looping
///   and termination are the Manager's responsibility; the orchestration is
///   not pre-scripted. This is the recommended pattern when work shape
///   depends on intermediate findings (most real research tasks).</item>
///
///   <item><b>Fixed Sequential pipeline</b> — the four-agent linear pipeline:
///   Researcher → Analyst → FactChecker → Writer, with a loop-back from
///   FactChecker to Researcher on unsupported claims. Simpler, more
///   predictable, lower-latency. Use when the work shape is known ahead of
///   time and you want zero Manager-side LLM overhead.</item>
/// </list>
///
/// Both entry points share the same four worker agents and the same
/// retriever-backed <c>search</c> tool, so swapping patterns is a one-line
/// change at the call site.
///
/// <para>
/// Note: this is the <em>agents-as-tools</em> pattern; it is NOT MAF's built-in
/// <c>Magentic</c> orchestration. The framework ships five built-in
/// orchestrations — <c>Sequential</c>, <c>Concurrent</c>, <c>Handoff</c>,
/// <c>GroupChat</c>, and <c>Magentic</c> — reach for those when you want the
/// framework to own the topology (e.g. a manager-with-ledger via the built-in
/// <c>Magentic</c> builder). Here the application owns it: a Manager agent that
/// calls workers as functions.
/// </para>
/// </summary>
public sealed class MultiAgentWorkflow
{
    private readonly IChatClient _chat;
    private readonly ChatClientAgent _researcher;
    private readonly ChatClientAgent _analyst;
    private readonly ChatClientAgent _factChecker;
    private readonly ChatClientAgent _writer;
    private readonly AIFunction _search;
    public int MaxLoops { get; }

    public MultiAgentWorkflow(IChatClient chat, IRetriever retriever, int maxLoops = 2)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(retriever);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLoops);

        _chat = chat;
        _search = AIFunctionFactory.Create(
            async (string q, int topK) =>
            {
                var hits = await retriever.RetrieveAsync(q, Math.Clamp(topK, 1, 10)).ConfigureAwait(false);
                return string.Join("\n", hits.Select((h, i) => $"[Source {i + 1}] {h.Chunk.Text}"));
            },
            name: "search",
            description: "Search the SmartDocs knowledge base.");

        _researcher = new ChatClientAgent(chat,
            name: "Researcher",
            description: "Finds relevant source material.",
            instructions: "You retrieve evidence. Use the search tool. Output a bullet list of source quotes.",
            tools: [_search]);
        _analyst = new ChatClientAgent(chat,
            name: "Analyst",
            description: "Synthesises findings into a draft answer.",
            instructions: "Given the Researcher's bullet list, write a draft answer with [Source N] inline citations.",
            tools: []);
        _factChecker = new ChatClientAgent(chat,
            name: "FactChecker",
            description: "Validates each claim against sources.",
            instructions: "For each sentence in the draft, reply SUPPORTED, PARTIALLY_SUPPORTED, or NOT_SUPPORTED with a one-line justification.",
            tools: []);
        _writer = new ChatClientAgent(chat,
            name: "Writer",
            description: "Polishes the final answer.",
            instructions: "Polish the draft into a concise, well-formatted final answer. Preserve all [Source N] citations.",
            tools: []);

        MaxLoops = maxLoops;
    }

    /// <summary>
    /// Default entry point — runs the agents-as-tools (LLM-as-manager) workflow.
    /// Equivalent to calling <see cref="RunAgentsAsToolsAsync"/>.
    /// </summary>
    public Task<string> RunAsync(string question, CancellationToken cancellationToken = default)
        => RunAgentsAsToolsAsync(question, cancellationToken);

    /// <summary>
    /// Agents-as-tools (LLM-as-manager) orchestration.
    /// A Manager <see cref="ChatClientAgent"/> receives the question and
    /// drives the four worker agents via tool calls. The Manager picks the
    /// next worker at each step based on what it has seen so far; the
    /// orchestration shape is decided by the Manager, not by the application.
    ///
    /// <para>
    /// This is the agents-as-tools pattern, not MAF's built-in <c>Magentic</c>
    /// orchestration. The Manager is an ordinary agent whose tools happen to be
    /// the worker agents; for a framework-owned manager-with-ledger, use MAF's
    /// <c>Magentic</c> builder instead (see <see cref="MultiAgentWorkflow"/>).
    /// </para>
    /// </summary>
    public async Task<string> RunAgentsAsToolsAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        // Wrap each worker as a tool the Manager can invoke.
        // Sessions are scoped to the call so worker state doesn't leak across questions.
        var researcherSession = await _researcher.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        var analystSession = await _analyst.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        var checkerSession = await _factChecker.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        var writerSession = await _writer.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        var callResearcher = AIFunctionFactory.Create(
            async (string directive) =>
            {
                var r = await _researcher.RunAsync(directive, researcherSession, options: null, cancellationToken: cancellationToken).ConfigureAwait(false);
                return r.Text ?? string.Empty;
            },
            name: "call_researcher",
            description: "Ask the Researcher to find source material. Pass a focused search directive.");

        var callAnalyst = AIFunctionFactory.Create(
            async (string directive) =>
            {
                var r = await _analyst.RunAsync(directive, analystSession, options: null, cancellationToken: cancellationToken).ConfigureAwait(false);
                return r.Text ?? string.Empty;
            },
            name: "call_analyst",
            description: "Ask the Analyst to draft an answer. Pass the question plus any findings.");

        var callFactChecker = AIFunctionFactory.Create(
            async (string directive) =>
            {
                var r = await _factChecker.RunAsync(directive, checkerSession, options: null, cancellationToken: cancellationToken).ConfigureAwait(false);
                return r.Text ?? string.Empty;
            },
            name: "call_fact_checker",
            description: "Ask the FactChecker to validate a draft against the sources. Pass sources + draft.");

        var callWriter = AIFunctionFactory.Create(
            async (string directive) =>
            {
                var r = await _writer.RunAsync(directive, writerSession, options: null, cancellationToken: cancellationToken).ConfigureAwait(false);
                return r.Text ?? string.Empty;
            },
            name: "call_writer",
            description: "Ask the Writer to polish a draft into the final answer. Pass the draft.");

        var manager = new ChatClientAgent(_chat,
            name: "Manager",
            description: "Plans and coordinates the SmartDocs research workflow.",
            instructions:
                "You orchestrate a research workflow over four specialist agents. " +
                "Call call_researcher to gather source quotes; call_analyst to draft an answer with [Source N] citations; " +
                "call_fact_checker to validate claims (reply contains SUPPORTED / PARTIALLY_SUPPORTED / NOT_SUPPORTED per sentence); " +
                "call_writer to polish. If the fact-checker flags NOT_SUPPORTED, call call_researcher again with a tighter directive, " +
                "then re-run the analyst and checker. After a successful check, call call_writer once and stop. " +
                $"Limit to {MaxLoops} research/analyst/check rounds before giving up. " +
                "Your final output is the polished answer from the Writer — return it verbatim, no commentary.",
            tools: [callResearcher, callAnalyst, callFactChecker, callWriter]);

        var managerSession = await manager.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        var result = await manager.RunAsync(question, managerSession, options: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.Text ?? string.Empty;
    }

    /// <summary>
    /// Sequential pipeline (alternative). Researcher → Analyst → FactChecker
    /// → Writer with a hard-coded loop-back from FactChecker to Researcher on
    /// NOT_SUPPORTED verdicts (max <see cref="MaxLoops"/> rounds).
    ///
    /// Lower per-turn cost than agents-as-tools (no Manager LLM call) and fully
    /// deterministic in shape. Use when the work pattern is known ahead of
    /// time and you don't need the Manager's runtime planning.
    /// </summary>
    public async Task<string> RunSequentialAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var researchSession = await _researcher.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        var analystSession = await _analyst.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        var checkerSession = await _factChecker.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        var writerSession = await _writer.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        string draft = string.Empty;
        for (int loop = 0; loop < MaxLoops; loop++)
        {
            var research = await _researcher.RunAsync(question, researchSession, options: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            var analystInput = $"Question: {question}\n\nResearcher findings:\n{research.Text}";
            var analyst = await _analyst.RunAsync(analystInput, analystSession, options: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            draft = analyst.Text ?? string.Empty;

            var checkerInput = $"Sources:\n{research.Text}\n\nDraft:\n{draft}";
            var check = await _factChecker.RunAsync(checkerInput, checkerSession, options: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            var verdict = check.Text ?? string.Empty;
            if (!verdict.Contains("NOT_SUPPORTED", StringComparison.Ordinal))
            {
                break;
            }
            // Otherwise loop and ask the Researcher to find more evidence.
        }

        var writerInput = $"Polish this draft for the user:\n{draft}";
        var final = await _writer.RunAsync(writerInput, writerSession, options: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        return final.Text ?? string.Empty;
    }
}
