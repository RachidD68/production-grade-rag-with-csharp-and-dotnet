namespace SmartDocs.Operations.Budgeting;

/// <summary>
/// Per-tenant budget policy for the <see cref="BudgetEnforcingPipeline"/>
/// (Ch 25). Three independent guards combine: a hard spend quota over a rolling
/// window, a request-rate ceiling, and a spend circuit-breaker that, once
/// tripped, refuses the tenant for a cool-down period rather than re-checking on
/// every call. All three are evaluated <em>before</em> the request reaches the
/// LLM — the cheapest place to stop an abusive or runaway tenant.
/// </summary>
/// <param name="SpendQuotaUsd">
/// The maximum USD a tenant may accumulate within the ledger's rolling window. A
/// request is refused when the tenant's rolling spend has already reached this
/// quota. Must be positive.
/// </param>
/// <param name="MaxRequests">
/// The maximum number of requests a tenant may make per <see cref="RateWindow"/>.
/// Must be positive.
/// </param>
/// <param name="RateWindow">The sliding window the request-rate ceiling applies over.</param>
/// <param name="BreakerCooldown">
/// Once the spend quota trips the breaker, how long the tenant stays refused
/// before the breaker re-arms (a budget circuit-breaker, not just a per-call
/// check — it avoids hammering an over-budget tenant with re-evaluation and gives
/// billing a window to reconcile).
/// </param>
public sealed record BudgetPolicy(
    double SpendQuotaUsd,
    int MaxRequests,
    TimeSpan RateWindow,
    TimeSpan BreakerCooldown)
{
    /// <summary>A conservative default: $5 / hour, 60 requests / minute, 5-minute breaker cool-down.</summary>
    public static BudgetPolicy Default { get; } = new(
        SpendQuotaUsd: 5.0,
        MaxRequests: 60,
        RateWindow: TimeSpan.FromMinutes(1),
        BreakerCooldown: TimeSpan.FromMinutes(5));
}

/// <summary>The reason a budget-enforced request was refused.</summary>
public enum BudgetDenialReason
{
    /// <summary>The tenant's rolling spend has reached or exceeded its quota.</summary>
    SpendQuotaExceeded,

    /// <summary>The tenant exceeded its request-rate ceiling.</summary>
    RateLimitExceeded,

    /// <summary>The spend circuit-breaker is open (cooling down after a quota trip).</summary>
    CircuitOpen,
}

/// <summary>
/// Thrown by <see cref="BudgetEnforcingPipeline"/> when a request is refused at
/// the budget boundary. It is a <em>block</em>, not an alert: the request never
/// reaches the model, so no tokens are spent on a tenant that is over budget,
/// rate-limited, or in breaker cool-down. The host maps this to an HTTP 429.
/// </summary>
public sealed class BudgetExceededException : Exception
{
    /// <summary>The tenant whose request was refused.</summary>
    public string Tenant { get; }

    /// <summary>Why the request was refused.</summary>
    public BudgetDenialReason Reason { get; }

    /// <summary>Create the exception for <paramref name="tenant"/> with the given <paramref name="reason"/>.</summary>
    public BudgetExceededException(string tenant, BudgetDenialReason reason)
        : base($"Budget enforcement blocked tenant '{tenant}': {reason}.")
    {
        Tenant = tenant;
        Reason = reason;
    }
}
