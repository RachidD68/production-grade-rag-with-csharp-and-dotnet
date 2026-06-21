using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace SmartDocs.Routing;

/// <summary>
/// The retrieve-or-not decision returned by <see cref="SelfRagDecider"/>.
/// </summary>
/// <param name="ShouldRetrieve">
/// <see langword="true"/> when the question needs grounding in the document
/// corpus; <see langword="false"/> when it is answerable from the model's
/// general knowledge.
/// </param>
/// <param name="Reasoning">A short justification for the decision.</param>
public sealed record RetrievalDecision(
    [property: JsonPropertyName("shouldRetrieve")] bool ShouldRetrieve,
    [property: JsonPropertyName("reasoning")] string Reasoning);

/// <summary>
/// Self-RAG's "retrieve-on-demand" gate (Asai et al., 2023), realised with
/// Microsoft.Extensions.AI structured output. The original paper trains a model
/// to emit a <c>[Retrieve]</c> reflection token; here we ask any
/// <see cref="IChatClient"/> for a typed <see cref="RetrievalDecision"/> instead.
/// A <see langword="false"/> result lets the pipeline skip retrieval for
/// general-knowledge questions ("what is 2+2?", "who wrote Hamlet?"), saving a
/// vector round-trip and avoiding irrelevant context; a <see langword="true"/>
/// result routes the question to the retriever as usual.
/// </summary>
public sealed class SelfRagDecider
{
    private readonly IChatClient _chat;

    private const string Prompt =
        """
        Decide whether answering the user's question requires retrieving documents
        from a private knowledge base, or whether it can be answered from your own
        general knowledge.

        Return ONLY a JSON object of this exact shape:

          { "shouldRetrieve": true | false, "reasoning": "one short sentence" }

        Set "shouldRetrieve" to true when the question asks about specific,
        organisation-internal, recent, or factual-lookup content that a general
        model would not reliably know. Set it to false for general knowledge,
        arithmetic, definitions, or chit-chat.

        Question: {0}
        """;

    public SelfRagDecider(IChatClient chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        _chat = chat;
    }

    /// <summary>
    /// Classify <paramref name="question"/> into a <see cref="RetrievalDecision"/>.
    /// Never throws on a model/parse failure: if structured output cannot be
    /// obtained, it returns a safe default of <c>ShouldRetrieve = true</c> so the
    /// pipeline errs toward grounding rather than hallucination.
    /// </summary>
    public async Task<RetrievalDecision> DecideAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var prompt = Prompt.Replace("{0}", question, StringComparison.Ordinal);

        // `useJsonSchemaResponseFormat: false` keeps this working with
        // local/Ollama models that lack native JSON-schema response support —
        // Microsoft.Extensions.AI then steers the model with a prompt-appended
        // schema and parses the reply into RetrievalDecision. Mirrors the Ch 13
        // EntityExtractor pattern.
        try
        {
            var typed = await _chat.GetResponseAsync<RetrievalDecision>(
                prompt,
                useJsonSchemaResponseFormat: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            // TryGetResult (NOT .Result, which throws on a failed parse).
            if (typed.TryGetResult(out var decision) &&
                decision is not null &&
                !string.IsNullOrWhiteSpace(decision.Reasoning))
            {
                return decision;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            // Structured output unavailable or unparseable — fall through to the
            // safe default below.
        }

        return new RetrievalDecision(ShouldRetrieve: true, Reasoning: "Defaulting to retrieval (decision unavailable).");
    }
}
