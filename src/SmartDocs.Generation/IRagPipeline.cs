namespace SmartDocs.Generation;

/// <summary>
/// The decorator seam over the RAG pipeline. <see cref="RagPipeline"/> is the
/// concrete three-stage orchestrator; this interface lets cross-cutting
/// concerns — response caching (Ch 21), tracing, A/B routing — wrap it
/// transparently without the call site knowing whether it holds the real
/// pipeline or a decorator. The two members mirror <see cref="RagPipeline"/>
/// exactly: a one-shot <see cref="AskAsync"/> and a streaming
/// <see cref="AskStreamingAsync"/> whose contract is
/// <c>Sources</c> first, then 0..n <c>Token</c> events, then <c>Done</c>.
/// </summary>
public interface IRagPipeline
{
    /// <summary>Answer <paramref name="question"/> in one shot, returning the grounded answer plus citations.</summary>
    Task<RagResponse> AskAsync(string question, CancellationToken ct = default);

    /// <summary>
    /// Answer <paramref name="question"/> as a stream of
    /// <see cref="RagStreamEvent"/>s: <c>Sources</c> first, then token events,
    /// then <c>Done</c>.
    /// </summary>
    IAsyncEnumerable<RagStreamEvent> AskStreamingAsync(string question, CancellationToken ct = default);
}
