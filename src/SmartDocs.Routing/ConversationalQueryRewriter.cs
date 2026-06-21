using Microsoft.Extensions.AI;

namespace SmartDocs.Routing;

/// <summary>
/// Rewrites a multi-turn conversational query into a self-contained query
/// the retriever can use without the chat history. Solves the
/// "follow-up problem" from Ch 12: e.g. given history
/// "What's the vacation policy?" → "20 days." and a follow-up "And in Paris?",
/// produces "What's the vacation policy in the Paris office?".
/// </summary>
public sealed class ConversationalQueryRewriter
{
    private readonly IChatClient _chat;
    private readonly int _maxHistoryTurns;

    private const string Prompt =
        """
        Rewrite the latest user message into a self-contained search query that does not
        require the chat history to be understood. Resolve pronouns and elliptical references.
        Reply with ONLY the rewritten query text — no preamble, no quotes.

        Chat history (oldest first):
        {0}

        Latest user message:
        {1}
        """;

    /// <summary>Create the rewriter.</summary>
    /// <param name="chat">The chat model that performs the rewrite.</param>
    /// <param name="maxHistoryTurns">
    /// How many of the most recent history turns to include in the prompt
    /// (default 8). Older turns are dropped so the prompt stays bounded as a
    /// conversation grows; summarising the dropped context instead of discarding
    /// it is a Chapter 12 exercise.
    /// </param>
    public ConversationalQueryRewriter(IChatClient chat, int maxHistoryTurns = 8)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHistoryTurns);
        _chat = chat;
        _maxHistoryTurns = maxHistoryTurns;
    }

    public async Task<string> RewriteAsync(
        IEnumerable<(string Role, string Text)> history,
        string latestUserMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrWhiteSpace(latestUserMessage);

        var historyList = history.ToList();
        if (historyList.Count == 0)
        {
            return latestUserMessage;
        }

        // Bound the prompt: keep only the most recent N turns. Older context is
        // dropped (rather than summarised — that is a Ch 12 exercise) so the
        // prompt size stays constant no matter how long the conversation runs.
        historyList = historyList.TakeLast(_maxHistoryTurns).ToList();

        var historyText = string.Join("\n", historyList.Select(h => $"{h.Role}: {h.Text}"));
        var prompt = Prompt
            .Replace("{0}", historyText, StringComparison.Ordinal)
            .Replace("{1}", latestUserMessage, StringComparison.Ordinal);
        var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = (response.Text ?? string.Empty).Trim().Trim('"');
        return string.IsNullOrEmpty(text) ? latestUserMessage : text;
    }
}
