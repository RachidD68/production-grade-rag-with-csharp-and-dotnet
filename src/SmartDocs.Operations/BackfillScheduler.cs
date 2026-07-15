using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Operations;

/// <summary>
/// The three re-embedding schedules a backfill can run on (Ch 22 §6). The
/// scheduler's <em>mechanics</em> — batch size, retry/backoff, checkpoint/resume —
/// are identical across all three; only the pacing differs, so the mode is a
/// configuration choice rather than three separate code paths.
/// </summary>
public enum BackfillSchedule
{
    /// <summary>Re-embed everything in one off-peak window (the nightly batch job).</summary>
    OffPeakNightlyBatch,

    /// <summary>Re-embed continuously at a low rate so the live system is never starved.</summary>
    BackgroundTrickle,

    /// <summary>Build a shadow cohort beside the live index, then cut over atomically.</summary>
    ParallelShadowThenCutover,
}

/// <summary>Options controlling a <see cref="BackfillScheduler"/> run.</summary>
/// <param name="OldModel">The embedding-model name whose chunks must be re-embedded.</param>
/// <param name="Schedule">Which of the three schedules to run.</param>
/// <param name="BatchSize">How many chunks to embed and upsert per batch. Must be positive.</param>
/// <param name="MaxRetries">
/// How many times to retry a transient failure on a single batch before giving up.
/// </param>
public sealed record BackfillOptions(
    string OldModel,
    BackfillSchedule Schedule = BackfillSchedule.OffPeakNightlyBatch,
    int BatchSize = 100,
    int MaxRetries = 3);

/// <summary>
/// A resumable progress cursor for a backfill. Persisted (in production) so a
/// multi-day job restarts where it left off rather than from zero. <c>Done</c> is
/// a set of already-re-embedded chunk ids, which makes resume idempotent: a chunk
/// in <c>Done</c> is skipped even if the source re-lists it.
/// </summary>
public sealed class BackfillProgress
{
    private readonly HashSet<string> _done;

    /// <summary>Create an empty checkpoint (a fresh run).</summary>
    public BackfillProgress()
        : this([])
    {
    }

    /// <summary>Create a checkpoint seeded with the chunk ids already completed (a resume).</summary>
    /// <param name="completedChunkIds">Chunk ids already re-embedded in a prior run.</param>
    public BackfillProgress(IEnumerable<string> completedChunkIds)
    {
        ArgumentNullException.ThrowIfNull(completedChunkIds);
        _done = new HashSet<string>(completedChunkIds, StringComparer.Ordinal);
    }

    /// <summary>Total chunks the run is expected to process (set when the run starts).</summary>
    public int Total { get; internal set; }

    /// <summary>The number of chunks re-embedded so far.</summary>
    public int Processed => _done.Count;

    /// <summary>True once every chunk in <see cref="Total"/> has been processed.</summary>
    public bool Done => Total > 0 && Processed >= Total;

    /// <summary>The set of chunk ids already completed — the resume key.</summary>
    public IReadOnlyCollection<string> CompletedChunkIds => _done;

    internal bool IsAlreadyDone(string chunkId) => _done.Contains(chunkId);

    internal void MarkDone(string chunkId) => _done.Add(chunkId);
}

/// <summary>
/// Re-embeds the chunks still stamped with an old embedding model
/// (<see cref="EmbeddedChunk.EmbeddingModel"/> — the migration linchpin from
/// Ch 22 §6) using the current <see cref="IEmbeddingService"/>, then upserts the
/// freshly embedded chunks back into the <see cref="IVectorStore"/>.
///
/// <para>
/// The source of "which chunks are on the old model" is supplied as an
/// <see cref="IAsyncEnumerable{T}"/> so this stays storage-agnostic and offline
/// testable: in production it is fed by a query such as
/// <c>WHERE EmbeddingModel == oldModel</c>; in tests it is fed by an in-memory
/// list. Mechanics (Ch 22 §6 #4): chunks are processed in batches of
/// <see cref="BackfillOptions.BatchSize"/>; a transient batch failure is retried
/// with exponential backoff up to <see cref="BackfillOptions.MaxRetries"/>; and a
/// <see cref="BackfillProgress"/> cursor records completed chunk ids so a resumed
/// run skips work already done (idempotent checkpoint/resume).
/// </para>
/// </summary>
public sealed class BackfillScheduler
{
    private readonly IEmbeddingService _embedder;
    private readonly IVectorStore _store;
    private readonly Func<int, TimeSpan> _backoff;

    /// <summary>
    /// Create a scheduler over the current embedding service and target vector store.
    /// </summary>
    /// <param name="embedder">The current-model embedding service used for re-embedding.</param>
    /// <param name="store">The vector store the re-embedded chunks are upserted into.</param>
    /// <param name="backoff">
    /// Optional retry/backoff hook mapping a zero-based attempt number to a delay.
    /// Defaults to an exponential schedule (200 ms · 2ⁿ). Supply a no-delay hook in
    /// tests to keep them fast.
    /// </param>
    public BackfillScheduler(
        IEmbeddingService embedder,
        IVectorStore store,
        Func<int, TimeSpan>? backoff = null)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(store);
        _embedder = embedder;
        _store = store;
        _backoff = backoff ?? (attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)));
    }

    /// <summary>
    /// Run (or resume) a backfill. Only chunks whose
    /// <see cref="EmbeddedChunk.EmbeddingModel"/> equals
    /// <see cref="BackfillOptions.OldModel"/> are re-embedded; chunks already on a
    /// newer model, and chunks recorded in <paramref name="progress"/>, are skipped.
    /// </summary>
    /// <param name="source">
    /// The chunks that are candidates for re-embedding (typically the result of a
    /// "still on the old model" query). Each is re-embedded with the current model.
    /// </param>
    /// <param name="options">Schedule + batch + retry configuration.</param>
    /// <param name="progress">
    /// The progress cursor. Pass a fresh <see cref="BackfillProgress"/> to start, or
    /// one seeded with completed ids to resume. Mutated in place as work completes.
    /// </param>
    /// <param name="cancellationToken">Cancels the run; a resumed run picks up the cursor.</param>
    /// <returns>The same <paramref name="progress"/> instance, now reflecting the run.</returns>
    public async Task<BackfillProgress> RunAsync(
        IAsyncEnumerable<EmbeddedChunk> source,
        BackfillOptions options,
        BackfillProgress? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OldModel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BatchSize);

        progress ??= new BackfillProgress();

        // Materialize the candidate set so Total is known up front and the
        // shadow-then-cutover mode can stage a full cohort before swapping.
        var candidates = new List<DocumentChunk>();
        await foreach (var embedded in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            // Only chunks still on the old model are eligible. A chunk already on a
            // newer model is left untouched (adapt-vs-re-embed decided per chunk).
            if (string.Equals(embedded.EmbeddingModel, options.OldModel, StringComparison.Ordinal))
            {
                candidates.Add(embedded.Chunk);
            }
        }

        progress.Total = candidates.Count;

        // Skip work already recorded in the cursor (idempotent resume).
        var pending = candidates.Where(c => !progress.IsAlreadyDone(c.ChunkId)).ToList();

        var staged = options.Schedule == BackfillSchedule.ParallelShadowThenCutover
            ? new List<EmbeddedChunk>(pending.Count)
            : null;

        for (int offset = 0; offset < pending.Count; offset += options.BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = pending.GetRange(offset, Math.Min(options.BatchSize, pending.Count - offset));

            var reembedded = await EmbedBatchWithRetryAsync(batch, options, cancellationToken).ConfigureAwait(false);

            if (staged is not null)
            {
                // Shadow mode: collect every re-embedded chunk; the cutover upsert
                // happens once at the end so the live index flips atomically.
                staged.AddRange(reembedded);
            }
            else
            {
                // Nightly-batch and background-trickle write each batch straight
                // through; the stable {DocumentId}#{ChunkIndex} ids make the upsert
                // an in-place overwrite of the old-model vector.
                await _store.UpsertAsync(reembedded, cancellationToken).ConfigureAwait(false);
            }

            foreach (var chunk in batch)
            {
                progress.MarkDone(chunk.ChunkId);
            }

            // Background-trickle paces itself between batches so the live system is
            // never starved; the batch and nightly modes run flat out.
            if (options.Schedule == BackfillSchedule.BackgroundTrickle && offset + options.BatchSize < pending.Count)
            {
                await Task.Delay(_backoff(0), cancellationToken).ConfigureAwait(false);
            }
        }

        if (staged is not null)
        {
            // Cutover: one upsert swaps the whole shadow cohort into the live index.
            await _store.UpsertAsync(staged, cancellationToken).ConfigureAwait(false);
        }

        return progress;
    }

    private async Task<List<EmbeddedChunk>> EmbedBatchWithRetryAsync(
        List<DocumentChunk> batch,
        BackfillOptions options,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var reembedded = new List<EmbeddedChunk>(batch.Count);
                foreach (var chunk in batch)
                {
                    reembedded.Add(await _embedder.EmbedAsync(chunk, cancellationToken).ConfigureAwait(false));
                }

                return reembedded;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < options.MaxRetries)
            {
                // Transient embedding-API failure (a 429 / 5xx in production): back
                // off and retry the whole batch. The retry is safe because
                // re-embedding is a pure function of the chunk text.
                await Task.Delay(_backoff(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
