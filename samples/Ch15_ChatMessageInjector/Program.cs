// Ch 15 — Loading retrieved context into an agent BEFORE the first turn
//
// Two ways to ground a MAF 1.16.0 ChatClientAgent in already-retrieved chunks,
// both shown here against the offline stub chat client:
//
//   1. Framework-native: a RetrievedChunksContextProvider (a MAF
//      MessageAIContextProvider) attached via ChatClientAgentOptions
//      .AIContextProviders. MAF prepends the provider's messages to the
//      request, so the chunks load before the first turn. This is the correct
//      primitive when retrieval runs upstream of the agent.
//
//   2. From-scratch: RunWithInjectedChunksAsync folds retriever output into a
//      preamble by hand — same effect, no provider abstraction, shown so the
//      mechanics are explicit.
//
// Both differ from MAF's IChatMessageInjector, whose injection happens MID
// function-loop (while the agent runs), not as a pre-turn preamble.
//
// Run: dotnet run --project samples/Ch15_ChatMessageInjector

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmartDocs.Agents;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

var retriever = new InMemoryDocsRetriever();
var chat = new EchoCitationClient();

var question = args.Length > 0
    ? string.Join(' ', args)
    : "What is the vacation policy?";

Console.WriteLine($"Question: {question}");
Console.WriteLine();

// ── Path 1: framework-native MAF context provider ───────────────────────────
// Retrieve upstream, then hand the chunks to the agent via a context provider.
var hits = await retriever.RetrieveAsync(question, topK: 3);
var chunks = hits.Select(h => h.Chunk).ToList();

var providerAgent = SmartDocsAgent.CreateWithRetrievedContext(chat, chunks, retriever);
var session = await providerAgent.CreateSessionAsync();
var providerResult = await providerAgent.RunAsync(question, session);

Console.WriteLine("[1] Framework-native context provider (MessageAIContextProvider):");
Console.WriteLine(providerResult.Text);
Console.WriteLine();

// ── Path 2: hand-built pre-turn preamble (from scratch) ─────────────────────
var injectorOptions = new ChunkInjectorOptions(retriever, TopK: 3);
var manualAnswer = await SmartDocsAgent
    .RunWithInjectedChunksAsync(SmartDocsAgent.Create(chat, retriever), question, injectorOptions);

Console.WriteLine("[2] Hand-built session preamble (no provider abstraction):");
Console.WriteLine(manualAnswer);

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
        // Detect injected preamble — it arrives either as a system-role context
        // message (Path 1) or as the first user message (Path 2). Both carry the
        // "Pre-retrieved context" marker.
        var injected = messages.Any(m => (m.Text ?? "").Contains("Pre-retrieved context"));

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
