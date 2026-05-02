using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;

namespace SmartDocs.Agents;

/// <summary>
/// The single-agent variant from Ch 19 — a <see cref="ChatClientAgent"/>
/// configured with the SmartDocs retrieval tools (vector + graph + web).
/// The agent autonomously decides which tool(s) to call per query.
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
