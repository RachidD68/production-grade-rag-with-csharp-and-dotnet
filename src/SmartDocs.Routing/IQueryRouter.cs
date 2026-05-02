namespace SmartDocs.Routing;

/// <summary>
/// Decides which silo (or combination of silos) a query targets. Used by
/// the orchestrator to dispatch the query to the right retriever(s).
/// </summary>
public sealed record RoutingDecision(
    IReadOnlyList<string> Silos,
    double Confidence,
    string Reasoning,
    string Strategy);

public interface IQueryRouter
{
    string Strategy { get; }
    Task<RoutingDecision> RouteAsync(string query, CancellationToken cancellationToken = default);
}
