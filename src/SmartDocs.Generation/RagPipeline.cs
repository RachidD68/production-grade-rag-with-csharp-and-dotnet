using System.Diagnostics;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Generation;

/// <summary>The grounded answer plus its citations + telemetry.</summary>
public sealed record RagResponse(
    string Answer,
    IReadOnlyList<RetrievalResult> Sources,
    long LatencyMs,
    string Strategy);

/// <summary>A streaming-event variant of <see cref="RagResponse"/>.</summary>
public sealed record RagStreamEvent(
    RagStreamEventKind Kind,
    string? Token = null,
    IReadOnlyList<RetrievalResult>? Sources = null);

public enum RagStreamEventKind { Sources, Token, Done }

/// <summary>
/// End-to-end retrieval-augmented-generation orchestrator.
/// Three-stage flow: retrieve → augment → generate. Both a one-shot
/// <see cref="AskAsync"/> and a streaming
/// <see cref="AskStreamingAsync"/> are surfaced.
/// </summary>
public sealed class RagPipeline
{
    private readonly IRetriever _retriever;
    private readonly PromptTemplateEngine _promptEngine;
    private readonly IChatClient _chat;
    public int TopK { get; set; } = 5;

    public RagPipeline(IRetriever retriever, PromptTemplateEngine promptEngine, IChatClient chat)
    {
        ArgumentNullException.ThrowIfNull(retriever);
        ArgumentNullException.ThrowIfNull(promptEngine);
        ArgumentNullException.ThrowIfNull(chat);
        _retriever = retriever;
        _promptEngine = promptEngine;
        _chat = chat;
    }

    public async Task<RagResponse> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var sw = Stopwatch.StartNew();
        var retrieved = await _retriever.RetrieveAsync(question, TopK, cancellationToken).ConfigureAwait(false);
        var (prompt, used) = _promptEngine.Build(question, retrieved);
        var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        sw.Stop();
        return new RagResponse(
            Answer: response.Text ?? string.Empty,
            Sources: used,
            LatencyMs: sw.ElapsedMilliseconds,
            Strategy: _retriever.Strategy);
    }

    public async IAsyncEnumerable<RagStreamEvent> AskStreamingAsync(
        string question,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var retrieved = await _retriever.RetrieveAsync(question, TopK, cancellationToken).ConfigureAwait(false);
        var (prompt, used) = _promptEngine.Build(question, retrieved);

        // 1) Sources first so the UI can render citations before the first token.
        yield return new RagStreamEvent(RagStreamEventKind.Sources, Sources: used);

        await foreach (var update in _chat.GetStreamingResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
            {
                yield return new RagStreamEvent(RagStreamEventKind.Token, Token: text);
            }
        }
        yield return new RagStreamEvent(RagStreamEventKind.Done);
    }
}
