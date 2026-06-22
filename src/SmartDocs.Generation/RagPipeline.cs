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

public enum RagStreamEventKind { Sources, Token, Done, Error }

/// <summary>
/// End-to-end retrieval-augmented-generation orchestrator.
/// Three-stage flow: retrieve → augment → generate. Both a one-shot
/// <see cref="AskAsync"/> and a streaming
/// <see cref="AskStreamingAsync"/> are surfaced.
/// </summary>
public sealed class RagPipeline : IRagPipeline
{
    // Generic, non-leaking message put on the wire when a stage faults. The
    // real exception is never surfaced to the browser (it could echo a poisoned
    // chunk or internal detail); callers should log it server-side instead.
    private const string StreamFailedMessage = "The answer could not be generated. Please try again.";

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

    public async Task<RagResponse> AskAsync(string question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var sw = Stopwatch.StartNew();
        var retrieved = await _retriever.RetrieveAsync(question, TopK, ct).ConfigureAwait(false);
        var (prompt, used) = _promptEngine.Build(question, retrieved);
        var response = await _chat.GetResponseAsync(prompt, cancellationToken: ct).ConfigureAwait(false);
        sw.Stop();
        return new RagResponse(
            Answer: response.Text ?? string.Empty,
            Sources: used,
            LatencyMs: sw.ElapsedMilliseconds,
            Strategy: _retriever.Strategy);
    }

    public async IAsyncEnumerable<RagStreamEvent> AskStreamingAsync(
        string question,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        // Stage 1+2 — retrieve and augment. A fault here is terminal: surface a
        // single Error event so the client renders a failure state instead of
        // hanging. Cancellation (the browser closed the SSE stream) is NOT an
        // error — it propagates so the in-flight work, and its billing, stop.
        string prompt;
        IReadOnlyList<RetrievalResult> used;
        var prepFailed = false;
        try
        {
            var retrieved = await _retriever.RetrieveAsync(question, TopK, ct).ConfigureAwait(false);
            (prompt, used) = _promptEngine.Build(question, retrieved);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            prompt = string.Empty;
            used = Array.Empty<RetrievalResult>();
            prepFailed = true;
        }

        if (prepFailed)
        {
            yield return new RagStreamEvent(RagStreamEventKind.Error, Token: StreamFailedMessage);
            yield break;
        }

        // 1) Sources first so the UI can render citations before the first token.
        yield return new RagStreamEvent(RagStreamEventKind.Sources, Sources: used);

        // Stage 3 — generate. Drive the stream through a manual enumerator so a
        // mid-stream fault becomes a clean Error event rather than a raw
        // exception torn through the SSE writer. (You cannot `yield` inside a
        // `catch`, hence the try/catch around MoveNextAsync with the yield outside.)
        var stream = _chat.GetStreamingResponseAsync(prompt, cancellationToken: ct);
        var enumerator = stream.GetAsyncEnumerator(ct);
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                RagStreamEvent? tokenEvent;
                var faulted = false;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }
                    ct.ThrowIfCancellationRequested();
                    var text = enumerator.Current.Text;
                    tokenEvent = string.IsNullOrEmpty(text)
                        ? null
                        : new RagStreamEvent(RagStreamEventKind.Token, Token: text);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    tokenEvent = null;
                    faulted = true;
                }

                // yield lives outside the catch — CS1631 forbids it inside one.
                if (faulted)
                {
                    yield return new RagStreamEvent(RagStreamEventKind.Error, Token: StreamFailedMessage);
                    yield break;
                }

                if (tokenEvent is not null)
                {
                    yield return tokenEvent;
                }
            }
        }

        yield return new RagStreamEvent(RagStreamEventKind.Done);
    }
}
