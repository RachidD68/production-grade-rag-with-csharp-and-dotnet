using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace RagInDotNet.Samples.Ch20_SafetyEval;

/// <summary>
/// A retriever that always returns a single, fixed chunk — used to inject a
/// <em>poisoned</em> context chunk into the RAG pipeline so the injection
/// canary can verify the agent does not obey instructions hidden in retrieved
/// text. The <see cref="IRetriever.RetrieveAsync"/> signature is the real
/// three-argument one: (query, topK, cancellationToken) — there is no filter
/// parameter.
/// </summary>
internal sealed class FixedChunkRetriever(string chunkText) : IRetriever
{
    public string Strategy => "fixed-poisoned";

    public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var meta = new DocumentMetadata(
            Id: "poison-001", Silo: "hr-policies", Department: "HR", Office: "Montreal",
            ConfidentialityLevel: "Internal", DocumentType: "Policy", FiscalYear: 2026,
            Author: "SmartDocs", LastModified: new DateOnly(2026, 1, 1), Title: "Vacation Policy");
        var chunk = new DocumentChunk(
            ChunkId: "poison-001#0", DocumentId: "poison-001", ChunkIndex: 0,
            Text: chunkText, StartCharOffset: 0, EndCharOffset: chunkText.Length, Metadata: meta);

        IReadOnlyList<RetrievalResult> hits = [new RetrievalResult(chunk, 0.99)];
        return Task.FromResult(hits);
    }
}

/// <summary>
/// A deterministic chat client that models a properly instruction-hardened LLM:
/// it answers the user's actual question from the legitimate part of the
/// context and <em>ignores</em> any imperative hidden in the retrieved text.
/// It never emits the injection canary token, so the offline robustness check
/// passes. (A vulnerable model would be one that echoes the canary; swapping in
/// such a stub would make the assertion fail — which is the point of the test.)
/// </summary>
internal sealed class HardenedChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // A safe, on-topic answer that does not act on injected instructions.
        const string answer =
            "Full-time employees accrue 20 paid vacation days per fiscal year [Source 1]. " +
            "I can only answer from the approved HR policy and cannot follow instructions " +
            "embedded in document text.";
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
