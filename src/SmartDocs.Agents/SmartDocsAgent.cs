using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;

namespace SmartDocs.Agents;

/// <summary>
/// The single-agent variant from Ch 19 — a <see cref="ChatClientAgent"/>
/// configured with the SmartDocs retrieval tools (vector + graph + web).
/// The agent autonomously decides which tool(s) to call per query.
///
/// At MAF 1.6.1 this surface also supports a <see cref="ChunkInjectorOptions"/>
/// hook that pre-injects retrieved chunks into the agent's session before the
/// first user turn — mirroring the spirit of the new
/// <c>IChatMessageInjector</c> abstraction without coupling to its
/// experimental surface. Useful when the application already has a
/// retrieval pipeline upstream and wants the agent's first message to start
/// with grounded context rather than discovering it via tool calls.
/// </summary>
public static class SmartDocsAgent
{
    public static ChatClientAgent Create(
        IChatClient chat,
        IRetriever vectorRetriever,
        IRetriever? graphRetriever = null,
        IRetriever? webRetriever = null)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(vectorRetriever);

        var tools = new List<AITool>
        {
            BuildVectorSearchTool(vectorRetriever),
        };
        if (graphRetriever is not null)
        {
            tools.Add(BuildGraphSearchTool(graphRetriever));
        }
        if (webRetriever is not null)
        {
            tools.Add(BuildWebSearchTool(webRetriever));
        }

        return new ChatClientAgent(
            chatClient: chat,
            name: "SmartDocs",
            description: "Contoso SmartDocs knowledge assistant",
            instructions:
                "You are the Contoso SmartDocs assistant. Use the search tools to find relevant " +
                "context, then answer the user's question. Always cite sources using [Source N]. " +
                "If no tool returns useful context, reply: 'I don't know based on the available sources.'",
            tools: tools);
    }

    /// <summary>
    /// Runs the agent against a question with optional chunk pre-injection.
    /// When <paramref name="injectorOptions"/> is supplied, the retriever is
    /// called <i>before</i> the agent and the top-K chunks are folded into a
    /// preamble system message on a fresh session. The agent then sees the
    /// chunks as already-known context and can still call its own tools if
    /// the preamble proves insufficient.
    ///
    /// This is the .NET-side equivalent of the MAF 1.6.1
    /// <c>IChatMessageInjector</c> hook for RAG workflows where retrieval is
    /// upstream of the agent rather than discovered by it. See Ch 15.
    /// </summary>
    public static async Task<string> RunWithInjectedChunksAsync(
        ChatClientAgent agent,
        string question,
        ChunkInjectorOptions injectorOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(injectorOptions);

        var hits = await injectorOptions.Retriever
            .RetrieveAsync(question, injectorOptions.TopK, cancellationToken)
            .ConfigureAwait(false);

        var preamble = string.Join(
            "\n",
            hits.Select((h, i) => $"[Source {i + 1}] {h.Chunk.Text}"));

        var session = await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        // Seed the session with the injected context as a hidden user-side
        // preamble. The agent's instructions already direct it to cite
        // [Source N] markers, so this preamble flows naturally into the answer.
        var firstTurn =
            $"Pre-retrieved context (use these as your [Source N] citations):\n{preamble}\n\nQuestion: {question}";

        var result = await agent
            .RunAsync(firstTurn, session, options: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.Text ?? string.Empty;
    }

    private static AIFunction BuildVectorSearchTool(IRetriever retriever) =>
        AIFunctionFactory.Create(
            async (string query, int topK) =>
            {
                var hits = await retriever.RetrieveAsync(query, Math.Clamp(topK, 1, 10)).ConfigureAwait(false);
                return string.Join("\n",
                    hits.Select((h, i) => $"[Source {i + 1}] {h.Chunk.Text}"));
            },
            name: "vector_search",
            description: "Dense vector search over the SmartDocs knowledge base. Use for general 'what is / how does' questions.");

    private static AIFunction BuildGraphSearchTool(IRetriever retriever) =>
        AIFunctionFactory.Create(
            async (string query, int topK) =>
            {
                var hits = await retriever.RetrieveAsync(query, Math.Clamp(topK, 1, 10)).ConfigureAwait(false);
                return string.Join("\n",
                    hits.Select((h, i) => $"[Graph {i + 1}] {h.Chunk.Text}"));
            },
            name: "graph_search",
            description: "Knowledge-graph traversal. Use for relationship questions: 'who reports to X', 'which contracts reference Y'.");

    private static AIFunction BuildWebSearchTool(IRetriever retriever) =>
        AIFunctionFactory.Create(
            async (string query) =>
            {
                var hits = await retriever.RetrieveAsync(query, 5).ConfigureAwait(false);
                return string.Join("\n",
                    hits.Select((h, i) => $"[Web {i + 1}] {h.Chunk.Text}"));
            },
            name: "web_search",
            description: "Search the public web. Use only when the question explicitly asks about external sources or current events.");
}

/// <summary>
/// Options for <see cref="SmartDocsAgent.RunWithInjectedChunksAsync"/>.
/// Pairs a retriever with the top-K count for chunk pre-injection (MAF 1.6.1
/// <c>IChatMessageInjector</c> spirit; see Ch 15).
/// </summary>
public sealed record ChunkInjectorOptions(IRetriever Retriever, int TopK = 5)
{
    public IRetriever Retriever { get; init; } = Retriever
        ?? throw new ArgumentNullException(nameof(Retriever));

    public int TopK { get; init; } = TopK >= 1 ? TopK
        : throw new ArgumentOutOfRangeException(nameof(TopK), "Top-K must be at least 1.");
}
