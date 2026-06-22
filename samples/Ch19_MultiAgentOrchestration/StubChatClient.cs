using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;

namespace RagInDotNet.Samples.Ch19_MultiAgentOrchestration;

/// <summary>
/// Deterministic, offline <see cref="IChatClient"/> for the orchestration sample.
/// It routes on the <c>ROLE:</c> marker each agent puts at the front of its
/// instructions (delivered through <see cref="ChatOptions.Instructions"/>) so every
/// agent in the graph produces distinct, plausible output with no model or API key.
///
/// <para>
/// The Researcher step runs a real dense retrieval against the offline corpus and
/// returns the top quotes as <c>[Source N]</c> markers, so genuine evidence flows
/// down the graph to the Writer's final answer. The remaining steps are pure
/// string transforms over the upstream output (the most recent user message, since
/// MAF reassigns each agent's reply to the user role for the next executor).
/// </para>
/// </summary>
internal sealed class StubChatClient : IChatClient
{
    private readonly IRetriever _retriever;

    public StubChatClient(IRetriever retriever)
    {
        ArgumentNullException.ThrowIfNull(retriever);
        _retriever = retriever;
    }

    public ChatClientMetadata Metadata { get; } = new("stub");

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var reply = await RespondAsync(messages, options, cancellationToken).ConfigureAwait(false);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, reply));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reply = await RespondAsync(messages, options, cancellationToken).ConfigureAwait(false);
        yield return new ChatResponseUpdate(ChatRole.Assistant, reply);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        // Nothing to dispose.
    }

    private async Task<string> RespondAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var list = messages.ToList();

        // Instructions arrive via ChatOptions.Instructions (not as a system message);
        // fall back to any system message for robustness.
        var role = options?.Instructions ?? string.Empty;
        if (role.Length == 0)
        {
            role = string.Join("\n", list.Where(m => m.Role == ChatRole.System).Select(m => m.Text));
        }

        // MAF reassigns each agent's reply to the user role for the next executor and
        // forwards the accumulating conversation, so the user messages are the chain of
        // prior agent outputs. The most recent is the immediate upstream output.
        var userTexts = list
            .Where(m => m.Role == ChatRole.User)
            .Select(m => m.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        var inbound = userTexts.Count > 0 ? userTexts[^1] : string.Empty;

        if (role.Contains("ROLE:researcher", StringComparison.Ordinal))
        {
            var hits = await _retriever.RetrieveAsync(FirstLine(inbound), 3, cancellationToken).ConfigureAwait(false);
            var quotes = string.Join("\n", hits.Select((h, i) => $"[Source {i + 1}] {h.Chunk.Text}"));
            return quotes.Length > 0 ? "Evidence:\n" + quotes : "No evidence found.";
        }

        if (role.Contains("ROLE:analyst", StringComparison.Ordinal))
        {
            var firstQuote = FirstSourceLine(inbound);
            return $"Draft answer: {firstQuote}";
        }

        if (role.Contains("ROLE:fact-checker", StringComparison.Ordinal))
        {
            return "Verification: every sentence SUPPORTED by [Source 1].";
        }

        if (role.Contains("ROLE:writer", StringComparison.Ordinal))
        {
            // The fact-checker's verdict is the most recent message; recover the
            // analyst's draft from earlier in the forwarded conversation.
            var draftLine = userTexts
                .FirstOrDefault(t => t.Contains("Draft answer:", StringComparison.Ordinal)) ?? inbound;
            var draft = draftLine
                .Replace("Draft answer: ", string.Empty, StringComparison.Ordinal)
                .Trim();
            return draft.Length > 0 ? draft : "I don't know based on the available sources.";
        }

        return FirstLine(inbound);
    }

    private static string FirstSourceLine(string text)
    {
        var sourceLine = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.Contains("[Source", StringComparison.Ordinal));
        return sourceLine ?? FirstLine(text);
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "the question";
        }
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.Length > 0) ?? text.Trim();
        return line.Length > 240 ? line[..240] : line;
    }
}
