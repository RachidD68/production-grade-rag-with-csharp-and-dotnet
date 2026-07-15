using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Filtering;

namespace SmartDocs.Routing.Filtering;

/// <summary>
/// The runnable self-query (a.k.a. self-querying) retriever. It asks an LLM to
/// extract a structured <see cref="ExtractedFilter"/> from the natural-language
/// query (<see cref="QueryConstructor"/>), cleans it against the closed
/// vocabulary (<see cref="FilterVocabulary"/>), and runs a <em>true pre-filter</em>
/// search through <see cref="IVectorStore.SearchAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// This stays a normal filter-free <see cref="IRetriever"/>: the filter is
/// produced internally, so callers and the <see cref="IRetriever"/> contract
/// never see a filter parameter.
/// </para>
/// <para>
/// <b>Relaxation.</b> An over-specified filter can empty the result set. When a
/// search returns nothing, the retriever drops the lowest-priority optional
/// constraint and retries, looping until results appear or only the security
/// filter remains. The drop order, least-important first, is:
/// <list type="number">
///   <item><c>FiscalYear</c> — most likely to over-constrain (a doc revised in a neighboring year is still relevant).</item>
///   <item><c>DocumentType</c> — the same content often appears under a related type.</item>
///   <item><c>Office</c> — location is frequently incidental to the answer.</item>
///   <item><c>Silo</c> — a coarse content bucket; nearby silos may hold the answer.</item>
///   <item><c>Department</c> — kept longest; the strongest topical signal.</item>
/// </list>
/// </para>
/// <para>
/// <b>Security is never relaxed.</b> The <see cref="SecurityContext"/> filter is
/// ANDed into every pass and is never a candidate for dropping, so relaxation can
/// only widen the <em>optional</em> scope, never the caller's authorization
/// ceiling. If even the security-only scope is empty, the retriever honestly
/// returns no results.
/// </para>
/// </remarks>
public sealed class SelfQueryRetriever : IRetriever
{
    private readonly IEmbeddingService _embeddings;
    private readonly IVectorStore _store;
    private readonly QueryConstructor _constructor;
    private readonly SecurityContext _security;

    /// <param name="embeddings">Embeds the (filter-stripped) free-text query.</param>
    /// <param name="store">The pre-filter-aware vector store.</param>
    /// <param name="constructor">Extracts the structured filter from the query via the LLM.</param>
    /// <param name="security">The auth-derived, never-relaxed security scope.</param>
    public SelfQueryRetriever(
        IEmbeddingService embeddings,
        IVectorStore store,
        QueryConstructor constructor,
        SecurityContext security)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(constructor);
        ArgumentNullException.ThrowIfNull(security);
        _embeddings = embeddings;
        _store = store;
        _constructor = constructor;
        _security = security;
    }

    /// <inheritdoc/>
    public string Strategy => "self-query";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        var extracted = FilterVocabulary.Clean(
            await _constructor.ExtractAsync(query, cancellationToken).ConfigureAwait(false));

        // Optional constraints, ordered MOST-important LAST so we can pop the
        // tail to drop the least-important first.
        var constraints = BuildOrderedConstraints(extracted);

        // Always-on security scope — never dropped.
        var security = _security.ToFilter();

        var embedText = string.IsNullOrWhiteSpace(extracted.FreeTextQuery) ? query : extracted.FreeTextQuery;
        var queryVec = await _embeddings.EmbedQueryAsync(embedText, cancellationToken).ConfigureAwait(false);

        // Relaxation loop: search with security AND all current constraints; on
        // empty, drop the lowest-priority constraint (the list tail) and retry.
        while (true)
        {
            var combined = constraints.Aggregate(security, (acc, c) => acc.And(c.Filter));
            var results = await _store
                .SearchAsync(queryVec, topK, combined, cancellationToken)
                .ConfigureAwait(false);

            if (results.Count > 0 || constraints.Count == 0)
            {
                // First non-empty result, or the security-only scope is genuinely
                // empty — the honest "no results in scope".
                return results;
            }

            // Drop the lowest-priority constraint and try again.
            constraints.RemoveAt(constraints.Count - 1);
        }
    }

    /// <summary>
    /// Builds the optional constraints in drop order. The list is ordered so the
    /// LAST element is the first to be dropped: FiscalYear, DocumentType, Office,
    /// Silo, Department (Department dropped last because it is the strongest
    /// topical signal). See the class remarks for the rationale.
    /// </summary>
    private static List<Constraint> BuildOrderedConstraints(ExtractedFilter f)
    {
        var ordered = new List<Constraint>();

        // Highest priority first (dropped last) ...
        if (!string.IsNullOrWhiteSpace(f.Department))
        {
            var v = f.Department;
            ordered.Add(new Constraint("Department", MetadataFilter.Where(m => m.Department == v)));
        }
        if (!string.IsNullOrWhiteSpace(f.Silo))
        {
            var v = f.Silo;
            ordered.Add(new Constraint("Silo", MetadataFilter.Where(m => m.Silo == v)));
        }
        if (!string.IsNullOrWhiteSpace(f.Office))
        {
            var v = f.Office;
            ordered.Add(new Constraint("Office", MetadataFilter.Where(m => m.Office == v)));
        }
        if (!string.IsNullOrWhiteSpace(f.DocumentType))
        {
            var v = f.DocumentType;
            ordered.Add(new Constraint("DocumentType", MetadataFilter.Where(m => m.DocumentType == v)));
        }
        // ... lowest priority last (dropped first).
        if (f.FiscalYear is { } year)
        {
            ordered.Add(new Constraint("FiscalYear", MetadataFilter.Where(m => m.FiscalYear == year)));
        }

        return ordered;
    }

    private readonly record struct Constraint(string Name, MetadataFilter Filter);
}
