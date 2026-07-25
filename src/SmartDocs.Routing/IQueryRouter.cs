namespace SmartDocs.Routing;

/// <summary>
/// Decides which silo (or combination of silos) a query targets. Used by
/// the orchestrator to dispatch the query to the right retriever(s).
/// </summary>
/// <param name="Silos">The silo (or silos) this query targets.</param>
/// <param name="Confidence">Router confidence in the decision, 0–1.</param>
/// <param name="Reasoning">Short human-readable justification, for logs and audits.</param>
/// <param name="Strategy">The routing method that produced the decision.</param>
/// <param name="Escalated">
/// True when producing this decision actually spent the expensive rung — an LLM
/// call or an embedding round-trip — as opposed to being answered by cheap rules.
/// Set from control flow by the router that made the call, NOT inferred from
/// <paramref name="Strategy"/>: a composed strategy name like
/// <c>multi(rule-based+llm-classifier)</c> is stamped on rule-based
/// short-circuits too, so matching on the name counts every decision as an
/// escalation and the cost metric reads ~100% forever.
/// </param>
public sealed record RoutingDecision(
    IReadOnlyList<string> Silos,
    double Confidence,
    string Reasoning,
    string Strategy,
    bool Escalated = false);

public interface IQueryRouter
{
    string Strategy { get; }
    Task<RoutingDecision> RouteAsync(string query, CancellationToken cancellationToken = default);
}
