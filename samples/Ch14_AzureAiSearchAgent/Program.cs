// Ch 14 — Azure AI Search RAG agent (MAF 1.6.1 reference pattern)
//
// This sample mirrors the official Microsoft Agent Framework 1.6.1 RAG
// reference: an Azure-hosted vector store fronted by a ChatClientAgent.
// We use a stand-in IRetriever that returns canned chunks so the sample
// runs offline; the production wiring (Azure AI Search VectorStore +
// EmbeddingGenerator + hybrid keyword retrieval) is documented in
// SmartDocs.Retrieval.AzureSearch — see Chapter 14 of the book.
//
// What this sample demonstrates:
//   1. A ChatClientAgent wired to a single vector-search tool.
//   2. The agent autonomously decides when to call the tool.
//   3. Inline [Source N] citation pattern in the answer.
//
// Run:   dotnet run --project samples/Ch14_AzureAiSearchAgent
// Stack: docker compose up ollama (provides the chat model)

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

var chat = BuildStubChatClient();
var retriever = new CannedAzureSearchRetriever();

var search = AIFunctionFactory.Create(
    async (string query, int topK) =>
    {
        var hits = await retriever.RetrieveAsync(query, Math.Clamp(topK, 1, 10));
        return string.Join("\n", hits.Select((h, i) => $"[Source {i + 1}] {h.Chunk.Text}"));
    },
    name: "azure_search",
    description: "Hybrid (vector + BM25) search against the Azure AI Search corpus.");

var agent = new ChatClientAgent(
    chatClient: chat,
    name: "ContosoSearchAgent",
    description: "Searches the Contoso corpus on Azure AI Search.",
    instructions:
        "You answer questions using only the azure_search tool's results. " +
        "Always cite sources as [Source N]. If the tool returns nothing useful, " +
        "say 'I don't know based on the available sources.'",
    tools: [search]);

var session = await agent.CreateSessionAsync();
var question = args.Length > 0
    ? string.Join(' ', args)
    : "How does Contoso's RAG pipeline use Azure AI Search?";

Console.WriteLine($"Question: {question}");
Console.WriteLine();

var result = await agent.RunAsync(question, session);
Console.WriteLine("Answer:");
Console.WriteLine(result.Text);

return 0;


// ── Stubs so the sample runs offline. ─────────────────────────────────────
// Replace BuildStubChatClient() with an IChatClient over Ollama / OpenAI /
// Azure OpenAI in real deployments; replace CannedAzureSearchRetriever with
// the production Azure AI Search adapter from SmartDocs.Retrieval.AzureSearch.

static IChatClient BuildStubChatClient()
{
    // Echo client — synthesises a plausible answer with a [Source 1] citation
    // so the orchestration shape is exercisable without a network call.
    return new EchoChatClient();
}

sealed class EchoChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastUser = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        var answer =
            "Contoso runs Azure AI Search behind a ChatClientAgent. Hybrid " +
            "queries fan out across vector and keyword indexes; the agent " +
            "calls azure_search and then composes a citation-grounded reply. " +
            "[Source 1]";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Streaming not implemented in this echo stub.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

sealed class CannedAzureSearchRetriever : IRetriever
{
    private static readonly DocumentMetadata Meta = new(
        Id: "contoso-rag-arch",
        Silo: "technical-docs",
        Department: "Engineering",
        Office: "Montreal",
        ConfidentialityLevel: "Internal",
        DocumentType: "ADR",
        FiscalYear: 2026,
        Author: "Contoso Platform Team",
        LastModified: new DateOnly(2026, 5, 14),
        Title: "Contoso RAG Architecture");

    private static readonly RetrievalResult[] Corpus =
    [
        new RetrievalResult(
            new DocumentChunk(
                ChunkId: "chunk-1",
                DocumentId: "contoso-rag-arch",
                ChunkIndex: 0,
                Text:
                    "Contoso's production RAG pipeline targets Azure AI Search as the primary store. " +
                    "Hybrid retrieval combines vector similarity with BM25; the semantic ranker re-orders " +
                    "the top-50 candidates before passing five chunks to the LLM.",
                StartCharOffset: 0,
                EndCharOffset: 280,
                Metadata: Meta),
            Score: 0.91),
        new RetrievalResult(
            new DocumentChunk(
                ChunkId: "chunk-2",
                DocumentId: "contoso-rag-arch",
                ChunkIndex: 1,
                Text:
                    "Each query is wrapped in a ChatClientAgent (Microsoft Agent Framework 1.6.1). " +
                    "The agent calls the azure_search tool, receives chunk text plus IDs, and emits " +
                    "a final answer that preserves [Source N] markers for downstream grounding.",
                StartCharOffset: 280,
                EndCharOffset: 560,
                Metadata: Meta),
            Score: 0.84),
    ];

    public string Strategy => "azure-ai-search-canned";

    public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query, int topK, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RetrievalResult>>(Corpus.Take(topK).ToList());
}
