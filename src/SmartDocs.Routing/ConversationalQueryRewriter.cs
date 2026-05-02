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

    public ConversationalQueryRewriter(IChatClient chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        _chat = chat;
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

        var historyText = string.Join("\n", historyList.Select(h => $"{h.Role}: {h.Text}"));
        var prompt = Prompt
            .Replace("{0}", historyText, StringComparison.Ordinal)
            .Replace("{1}", latestUserMessage, StringComparison.Ordinal);
        var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = (response.Text ?? string.Empty).Trim().Trim('"');
        return string.IsNullOrEmpty(text) ? latestUserMessage : text;
    }
}
