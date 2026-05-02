using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;

namespace SmartDocs.Agents;

/// <summary>
/// The four-agent SmartDocs workflow from Ch 19:
///
///   Researcher  -> AnalystOK ? Analyst : ResearchAgain
///   Analyst     -> FactChecker
///   FactChecker -> all-supported ? Writer : back to Researcher (max 2 loops)
///   Writer      -> final response
///
/// Phase-5 implementation runs the four agents sequentially with a simple
/// loop guard. Wiring as a graph-based workflow via the official
/// <c>Microsoft.Agents.AI.Workflows</c> WorkflowBuilder is shown in
/// <c>BuildSequential</c> below; production projects should follow
/// the WorkflowBuilder + handoffs API documented in the MAF samples.
/// </summary>
public sealed class MultiAgentWorkflow
{
    private readonly ChatClientAgent _researcher;
    private readonly ChatClientAgent _analyst;
    private readonly ChatClientAgent _factChecker;
    private readonly ChatClientAgent _writer;
    public int MaxLoops { get; }

    public MultiAgentWorkflow(IChatClient chat, IRetriever retriever, int maxLoops = 2)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(retriever);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLoops);

        var search = AIFunctionFactory.Create(
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
            tools: [search]);
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

    public async Task<string> RunAsync(string question, CancellationToken cancellationToken = default)
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
