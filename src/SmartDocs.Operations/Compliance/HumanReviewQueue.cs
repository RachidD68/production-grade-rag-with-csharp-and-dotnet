using System.Collections.Concurrent;

namespace SmartDocs.Operations.Compliance;

/// <summary>
/// One answer queued for human review. Carries the answer, the model's
/// confidence, why it was flagged, and — once a reviewer acts — the reviewer's
/// edit. A resolved item with an edit becomes a future eval case: the
/// (query, reviewer-approved answer) pair is exactly a golden-set row.
/// </summary>
/// <param name="ItemId">Stable id for the queued item.</param>
/// <param name="QueryId">The originating query id.</param>
/// <param name="Query">The user's question.</param>
/// <param name="Answer">The machine answer awaiting review.</param>
/// <param name="Confidence">The model's confidence in the answer.</param>
/// <param name="Reason">Why the item was routed to review (low confidence, high-risk tier, …).</param>
/// <param name="EnqueuedAt">When it entered the queue.</param>
/// <param name="ReviewerEdit">The reviewer's corrected answer; null until reviewed.</param>
/// <param name="ReviewedAt">When it was resolved; null until reviewed.</param>
public sealed record ReviewItem(
    string ItemId,
    string QueryId,
    string Query,
    string Answer,
    double Confidence,
    string Reason,
    DateTimeOffset EnqueuedAt,
    string? ReviewerEdit = null,
    DateTimeOffset? ReviewedAt = null)
{
    /// <summary>Whether a reviewer has resolved this item.</summary>
    public bool IsResolved => ReviewedAt is not null;
}

/// <summary>
/// In-memory human-review queue. Supports enqueue, dequeue (claim the oldest
/// pending item), and override (a reviewer resolves an item with their edit). A
/// resolved item is captured as a future eval case via
/// <see cref="ResolvedCases"/>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The 'Queue' suffix is the chapter's domain term for the human-review queue; the type genuinely is a queue.")]
public sealed class HumanReviewQueue
{
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly ConcurrentDictionary<string, ReviewItem> _items = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public HumanReviewQueue(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Number of items still awaiting review.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Enqueue an answer for review. Returns the created item.</summary>
    public ReviewItem Enqueue(string queryId, string query, string answer, double confidence, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var item = new ReviewItem(
            ItemId: Guid.NewGuid().ToString("n"),
            QueryId: queryId,
            Query: query,
            Answer: answer,
            Confidence: confidence,
            Reason: reason,
            EnqueuedAt: _timeProvider.GetUtcNow());
        _items[item.ItemId] = item;
        _pending.Enqueue(item.ItemId);
        return item;
    }

    /// <summary>
    /// Claim the oldest pending, unresolved item, or <see langword="null"/> when
    /// the queue is empty. Skips items already resolved out-of-band.
    /// </summary>
    public ReviewItem? Dequeue()
    {
        while (_pending.TryDequeue(out var id))
        {
            if (_items.TryGetValue(id, out var item) && !item.IsResolved)
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>
    /// A reviewer overrides an item with their corrected answer. Returns the
    /// resolved item, or <see langword="null"/> when the id is unknown.
    /// </summary>
    public ReviewItem? Override(string itemId, string reviewerEdit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentNullException.ThrowIfNull(reviewerEdit);

        if (!_items.TryGetValue(itemId, out var item))
        {
            return null;
        }

        var resolved = item with { ReviewerEdit = reviewerEdit, ReviewedAt = _timeProvider.GetUtcNow() };
        _items[itemId] = resolved;
        return resolved;
    }

    /// <summary>
    /// The resolved items as future eval cases: (query, reviewer-approved answer)
    /// pairs ready to seed a golden set.
    /// </summary>
    public IReadOnlyList<ReviewItem> ResolvedCases =>
        [.. _items.Values.Where(i => i.IsResolved).OrderBy(i => i.ReviewedAt)];
}
