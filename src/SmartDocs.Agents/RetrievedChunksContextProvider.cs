using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Documents;

namespace SmartDocs.Agents;

/// <summary>
/// A framework-native MAF 1.10.0 <see cref="MessageAIContextProvider"/> that
/// pre-loads retrieved <see cref="DocumentChunk"/> text into a
/// <see cref="ChatClientAgent"/>'s context BEFORE the first user turn.
///
/// <para>
/// This is the correct primitive for "load context up front": MAF invokes
/// <see cref="ProvideMessagesAsync"/> on every run and prepends the returned
/// messages to the request, so the agent starts grounded in the supplied
/// chunks. Contrast with <c>IChatMessageInjector</c> /
/// <see cref="MessageInjectingChatClient"/>, whose semantics are mid-loop —
/// they inject messages into the agent's <em>function-calling loop</em> as it
/// runs (e.g. after a tool call), not as a pre-turn preamble. Use a context
/// provider when retrieval is upstream of the agent; use the injector when you
/// need to nudge the loop while it executes.
/// </para>
///
/// <para>
/// Attach it via <see cref="ChatClientAgentOptions.AIContextProviders"/> on the
/// <see cref="ChatClientAgent"/> constructor — see
/// <see cref="SmartDocsAgent.CreateWithRetrievedContext"/>.
/// </para>
/// </summary>
public sealed class RetrievedChunksContextProvider : MessageAIContextProvider
{
    private readonly IReadOnlyList<DocumentChunk> _chunks;

    /// <summary>
    /// Create a provider that injects <paramref name="chunks"/> as grounding
    /// context. The chunks are typically the top-K output of a retrieval
    /// pipeline already run for the question.
    /// </summary>
    public RetrievedChunksContextProvider(IReadOnlyList<DocumentChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        _chunks = chunks;
    }

    /// <summary>
    /// Supplies the retrieved chunks as a single system-role context message
    /// before each run. MAF prepends the returned message(s) to the request, so
    /// the agent sees the chunks as already-known context and can cite them with
    /// the <c>[Source N]</c> markers its instructions ask for.
    /// </summary>
    protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_chunks.Count == 0)
        {
            return ValueTask.FromResult(Enumerable.Empty<ChatMessage>());
        }

        var preamble = string.Join(
            "\n",
            _chunks.Select((c, i) => $"[Source {i + 1}] {c.Text}"));

        var message = new ChatMessage(
            ChatRole.System,
            "Pre-retrieved context (use these as your [Source N] citations):\n" + preamble);

        return ValueTask.FromResult<IEnumerable<ChatMessage>>([message]);
    }
}
