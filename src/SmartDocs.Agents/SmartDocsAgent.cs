using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Agents;

/// <summary>
/// The single-agent variant from Ch 19 — a <see cref="ChatClientAgent"/>
/// configured with the SmartDocs retrieval tools (vector + graph + web).
/// The agent autonomously decides which tool(s) to call per query.
///
/// <para>
/// Ch 15 shows two ways to start the agent already grounded in retrieved
/// context, with deliberately different semantics:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <see cref="CreateWithRetrievedContext"/> attaches a framework-native
///     MAF 1.10.0 <see cref="RetrievedChunksContextProvider"/>
///     (a <see cref="MessageAIContextProvider"/>) via
///     <see cref="ChatClientAgentOptions.AIContextProviders"/>. MAF prepends the
///     provider's messages to the request, so the chunks are loaded BEFORE the
///     first turn — the right primitive for "retrieval is upstream of the agent".
///   </description></item>
///   <item><description>
///     <see cref="RunWithInjectedChunksAsync"/> is the from-scratch path: it
///     builds the same grounding by hand as a session preamble, with no
///     dependency on the provider abstraction. Kept for readers who want to see
///     the mechanics explicitly.
///   </description></item>
/// </list>
/// <para>
/// Both differ from MAF's <c>IChatMessageInjector</c> /
/// <see cref="MessageInjectingChatClient"/>, whose semantics are mid-loop:
/// it injects messages into the agent's function-calling loop while it runs,
/// not as a pre-turn preamble.
/// </para>
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
    /// Builds a <see cref="ChatClientAgent"/> with the SmartDocs retrieval tools
    /// AND a framework-native <see cref="RetrievedChunksContextProvider"/>
    /// attached via <see cref="ChatClientAgentOptions.AIContextProviders"/>. The
    /// provider pre-loads <paramref name="retrievedChunks"/> into the agent's
    /// context before the first turn — the correct MAF 1.10.0 primitive for
    /// "load context up front" when retrieval ran upstream of the agent. The
    /// agent can still call its own tools if the pre-loaded context proves
    /// insufficient.
    /// </summary>
    public static ChatClientAgent CreateWithRetrievedContext(
        IChatClient chat,
        IReadOnlyList<DocumentChunk> retrievedChunks,
        IRetriever vectorRetriever,
        IRetriever? graphRetriever = null,
        IRetriever? webRetriever = null)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(retrievedChunks);
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

        var options = new ChatClientAgentOptions
        {
            Name = "SmartDocs",
            Description = "Contoso SmartDocs knowledge assistant",
            ChatOptions = new ChatOptions
            {
                Instructions =
                    "You are the Contoso SmartDocs assistant. Use the search tools to find relevant " +
                    "context, then answer the user's question. Always cite sources using [Source N]. " +
                    "If no tool returns useful context, reply: 'I don't know based on the available sources.'",
                Tools = tools,
            },
            AIContextProviders = [new RetrievedChunksContextProvider(retrievedChunks)],
        };

        return new ChatClientAgent(chat, options);
    }

    /// <summary>
    /// Runs the agent against a question with optional chunk pre-injection,
    /// built by hand (no context-provider abstraction). When
    /// <paramref name="injectorOptions"/> is supplied, the retriever is called
    /// <i>before</i> the agent and the top-K chunks are folded into a preamble
    /// message on a fresh session. The agent then sees the chunks as
    /// already-known context and can still call its own tools if the preamble
    /// proves insufficient.
    ///
    /// This is the from-scratch counterpart to
    /// <see cref="CreateWithRetrievedContext"/> (which does the same thing
    /// through MAF's <see cref="RetrievedChunksContextProvider"/>). Both load
    /// context BEFORE the first turn — unlike MAF's <c>IChatMessageInjector</c>,
    /// whose injection happens mid function-loop. See Ch 15.
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
/// Pairs a retriever with the top-K count for the hand-built pre-turn
/// preamble (see Ch 15; the framework-native equivalent is
/// <see cref="RetrievedChunksContextProvider"/>).
/// </summary>
public sealed record ChunkInjectorOptions(IRetriever Retriever, int TopK = 5)
{
    public IRetriever Retriever { get; init; } = Retriever
        ?? throw new ArgumentNullException(nameof(Retriever));

    public int TopK { get; init; } = TopK >= 1 ? TopK
        : throw new ArgumentOutOfRangeException(nameof(TopK), "Top-K must be at least 1.");
}
