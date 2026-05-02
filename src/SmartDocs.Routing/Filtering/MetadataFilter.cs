using System.Linq.Expressions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Routing.Filtering;

/// <summary>
/// Lightweight metadata filter — wraps an
/// <see cref="Expression{TDelegate}"/> over <see cref="DocumentMetadata"/>
/// so callers can compose filters in C# (<see cref="MetadataFilter.Where"/>)
/// and adapter code can compile them to provider-specific filter languages
/// (Qdrant filter JSON, Azure AI Search OData, pgvector WHERE).
/// </summary>
public sealed class MetadataFilter
{
    public Expression<Func<DocumentMetadata, bool>> Predicate { get; }
    private readonly Func<DocumentMetadata, bool> _compiled;

    private MetadataFilter(Expression<Func<DocumentMetadata, bool>> predicate)
    {
        Predicate = predicate;
        _compiled = predicate.Compile();
    }

    /// <summary>Build a filter from a C# predicate expression.</summary>
    public static MetadataFilter Where(Expression<Func<DocumentMetadata, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new MetadataFilter(predicate);
    }

    /// <summary>The pass-everything filter.</summary>
    public static MetadataFilter All { get; } = new(_ => true);

    /// <summary>Apply the filter in process — used by the in-memory store and the eval harness.</summary>
    public bool Matches(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return _compiled(metadata);
    }

    /// <summary>Logical AND of two filters.</summary>
    public MetadataFilter And(MetadataFilter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var p = Expression.Parameter(typeof(DocumentMetadata));
        var combined = Expression.AndAlso(
            Expression.Invoke(Predicate, p),
            Expression.Invoke(other.Predicate, p));
        return new MetadataFilter(Expression.Lambda<Func<DocumentMetadata, bool>>(combined, p));
    }
}
