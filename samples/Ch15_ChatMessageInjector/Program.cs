// Ch 15 — IChatMessageInjector pattern (MAF 1.6.1)
//
// Background: MAF 1.6.1 introduced IChatMessageInjector, an abstraction for
// injecting messages into an agent's function loop at runtime. The hook
// matters for RAG because retrieval is usually upstream of the agent — you
// want the agent to start with context already in hand, not discover it
// through tool calls.
//
// This sample shows the .NET-side equivalent: SmartDocsAgent.RunWithInjectedChunksAsync
// folds retriever output into a preamble message before the agent's first
// turn. Same effect, no dependency on the experimental injector surface.
//
// Run: dotnet run --project samples/Ch15_ChatMessageInjector

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmartDocs.Agents;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

var retriever = new InMemoryDocsRetriever();
var chat = new EchoCitationClient();

var agent = SmartDocsAgent.Create(chat, retriever);

// Inject the top-3 chunks before the first user turn:
var injectorOptions = new ChunkInjectorOptions(retriever, TopK: 3);

var question = args.Length > 0
    ? string.Join(' ', args)
    : "What is the vacation policy?";

Console.WriteLine($"Question: {question}");
Console.WriteLine();

var answer = await SmartDocsAgent
    .RunWithInjectedChunksAsync(agent, question, injectorOptions);

Console.WriteLine("Answer (chunks pre-injected; agent did not call its own tools):");
Console.WriteLine(answer);

return 0;


// ── Stubs ─────────────────────────────────────────────────────────────────

sealed class InMemoryDocsRetriever : IRetriever
{
    private static readonly DocumentMetadata Meta = new(
        Id: "hr-101", Silo: "hr-policies", Department: "HR", Office: "Montreal",
        ConfidentialityLevel: "Internal", DocumentType: "Policy",
        FiscalYear: 2026, Author: "Contoso HR",
        LastModified: new DateOnly(2026, 1, 15),
        Title: "Vacation Policy");

    private static readonly DocumentChunk[] Chunks =
    [
        new DocumentChunk("c1", "hr-101", 0,
            "Full-time employees accrue 20 vacation days per calendar year. Unused days carry over up to 5 days.",
            0, 100, Meta),
        new DocumentChunk("c2", "hr-101", 1,
            "Vacation requests must be submitted at least two weeks in advance through the HR portal.",
            100, 200, Meta),
        new DocumentChunk("c3", "hr-101", 2,
            "Approval is required from the direct manager; HR reviews requests longer than 10 consecutive days.",
            200, 300, Meta),
    ];

    public string Strategy => "in-memory-canned";

    public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query, int topK, CancellationToken cancellationToken = default)
    {
        var hits = Chunks
            .Take(topK)
            .Select((c, i) => new RetrievalResult(c, Score: 1.0 - i * 0.05))
            .ToList();
        return Task.FromResult<IReadOnlyList<RetrievalResult>>(hits);
    }
}

sealed class EchoCitationClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Detect injected preamble — the first user message will start with the prefix.
        var firstUser = messages.FirstOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        var injected = firstUser.Contains("Pre-retrieved context");

        var answer = injected
            ? "Full-time employees get 20 vacation days per year, with up to 5 days carryover [Source 1]. " +
              "Requests are submitted at least two weeks in advance [Source 2] and require manager approval [Source 3]."
            : "I would need to search for that.";

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
