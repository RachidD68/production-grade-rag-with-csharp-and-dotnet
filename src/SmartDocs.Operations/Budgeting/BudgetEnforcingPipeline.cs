using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SmartDocs.Generation;
using SmartDocs.Performance.Cost;
using SmartDocs.Security.Input;

namespace SmartDocs.Operations.Budgeting;

/// <summary>
/// A pipeline-boundary budget governor (Ch 25 §"Budget enforcement — not just
/// alerting"). Where the Ch 21 cost telemetry only <em>observes</em> spend, this
/// decorator <em>acts</em> on it: it sits in front of the real
/// <see cref="IRagPipeline"/> and refuses a tenant that is over its rolling spend
/// quota, over its request-rate ceiling, or inside a spend circuit-breaker
/// cool-down — throwing a <see cref="BudgetExceededException"/> before a single
/// token is spent. It is the enforcement teeth behind the dashboard's alerts.
///
/// <para>
/// Three reused building blocks (no new mechanism invented):
/// <list type="bullet">
///   <item><description>
///     The Ch 23 <see cref="RateLimiter"/> for the per-tenant request-rate
///     ceiling (the same sliding-window limiter the security layer ships).
///   </description></item>
///   <item><description>
///     The Ch 21 cost primitives — <see cref="TokenPricing"/> and
///     <see cref="CostMeter"/> — to price each answer and keep the dashboard's
///     per-tenant cost metric flowing while this layer enforces.
///   </description></item>
///   <item><description>
///     A <see cref="TenantSpendLedger"/> for the rolling per-tenant total the
///     quota and breaker decisions read.
///   </description></item>
/// </list>
/// </para>
///
/// <para>
/// Metering is post-pay: the dollar cost of an answer is known only after it is
/// generated, so a request is admitted on the tenant's <em>already-accumulated</em>
/// rolling spend and the breaker trips the moment that total crosses the quota —
/// stopping the <em>next</em> call. This is the standard runaway-tenant guard: a
/// single expensive answer can overshoot once, but a tenant cannot keep spending
/// past its quota. <see cref="TimeProvider"/> is injected so the breaker
/// cool-down and the rate window are deterministic in tests.
/// </para>
/// </summary>
public sealed class BudgetEnforcingPipeline : IRagPipeline
{
    private readonly IRagPipeline _inner;
    private readonly BudgetPolicy _policy;
    private readonly TenantSpendLedger _ledger;
    private readonly RateLimiter _rateLimiter;
    private readonly Func<RagResponse, double> _costEstimator;
    private readonly Func<string> _tenantAccessor;
    private readonly Func<string> _userAccessor;
    private readonly TimeProvider _timeProvider;

    // tenant -> the UTC instant its spend breaker re-arms. Absent = closed.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _breakerOpenUntil =
        new(StringComparer.Ordinal);

    /// <summary>Create the governor over an inner pipeline.</summary>
    /// <param name="inner">The real pipeline invoked once a request is admitted.</param>
    /// <param name="policy">The per-tenant budget policy (quota, rate, breaker cool-down).</param>
    /// <param name="ledger">The rolling spend ledger the quota reads and each answer's cost is recorded into.</param>
    /// <param name="tenantAccessor">
    /// Resolves the current tenant id (from the ambient request context in
    /// production). Defaults to a single <c>"default"</c> tenant when omitted.
    /// </param>
    /// <param name="pricing">
    /// Token pricing used by the default cost estimator. Ignored when
    /// <paramref name="costEstimator"/> is supplied. Defaults to
    /// <see cref="TokenPricing.Default"/>.
    /// </param>
    /// <param name="costEstimator">
    /// Prices a completed <see cref="RagResponse"/> in USD. Defaults to a length-based
    /// estimate (answer characters ≈ tokens ÷ 4) at the output price — enough to make
    /// the rolling total move; a host with real token usage supplies an exact estimator.
    /// </param>
    /// <param name="userAccessor">
    /// Resolves the current user id for the rate limiter's <c>(tenant, user)</c> key.
    /// Defaults to <c>"all"</c> so the limiter degrades to per-tenant when no user is in scope.
    /// </param>
    /// <param name="timeProvider">Clock for the breaker cool-down. Defaults to <see cref="TimeProvider.System"/>.</param>
    public BudgetEnforcingPipeline(
        IRagPipeline inner,
        BudgetPolicy policy,
        TenantSpendLedger ledger,
        Func<string>? tenantAccessor = null,
        TokenPricing? pricing = null,
        Func<RagResponse, double>? costEstimator = null,
        Func<string>? userAccessor = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.SpendQuotaUsd);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.MaxRequests);

        _inner = inner;
        _policy = policy;
        _ledger = ledger;
        _tenantAccessor = tenantAccessor ?? (static () => "default");
        _timeProvider = timeProvider ?? TimeProvider.System;

        var effectivePricing = pricing ?? TokenPricing.Default;
        _costEstimator = costEstimator ?? (response => EstimateCost(response, effectivePricing));

        _userAccessor = userAccessor ?? (static () => "all");
        _rateLimiter = new RateLimiter(policy.MaxRequests, policy.RateWindow, _timeProvider);
    }

    /// <inheritdoc />
    public async Task<RagResponse> AskAsync(string question, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var tenant = _tenantAccessor();
        Admit(tenant);

        var response = await _inner.AskAsync(question, ct).ConfigureAwait(false);
        RecordSpend(tenant, response);
        return response;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RagStreamEvent> AskStreamingAsync(
        string question,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var tenant = _tenantAccessor();

        // Admit BEFORE opening the stream so an over-budget tenant is refused
        // without any upstream work. The throw surfaces to the caller (the SSE
        // writer maps it to an error event / 429) exactly as a synchronous block.
        Admit(tenant);

        var answer = new System.Text.StringBuilder();
        var sources = Array.Empty<Core.Documents.RetrievalResult>() as IReadOnlyList<Core.Documents.RetrievalResult>;
        var faulted = false;

        await foreach (var ev in _inner.AskStreamingAsync(question, ct).ConfigureAwait(false))
        {
            switch (ev.Kind)
            {
                case RagStreamEventKind.Sources when ev.Sources is not null:
                    sources = ev.Sources;
                    break;
                case RagStreamEventKind.Token when ev.Token is not null:
                    answer.Append(ev.Token);
                    break;
                case RagStreamEventKind.Error:
                    faulted = true;
                    break;
                default:
                    break;
            }

            yield return ev;
        }

        if (!faulted)
        {
            // Price the reassembled answer once the stream completes so the rolling
            // total moves for streaming traffic too (the breaker stops the next call).
            RecordSpend(tenant, new RagResponse(answer.ToString(), sources, LatencyMs: 0, Strategy: "stream"));
        }
    }

    private void Admit(string tenant)
    {
        var now = _timeProvider.GetUtcNow();

        // 1. Spend circuit-breaker: if open and still cooling down, refuse fast.
        if (_breakerOpenUntil.TryGetValue(tenant, out var openUntil))
        {
            if (now < openUntil)
            {
                throw new BudgetExceededException(tenant, BudgetDenialReason.CircuitOpen);
            }

            // Cool-down elapsed — re-arm by clearing the breaker (only if unchanged).
            _breakerOpenUntil.TryRemove(new KeyValuePair<string, DateTimeOffset>(tenant, openUntil));
        }

        // 2. Rate ceiling (Ch 23 RateLimiter). A burst is refused before the quota
        //    check so an abusive tenant cannot even probe the spend total rapidly.
        if (!_rateLimiter.TryAcquire(tenant, _userAccessor()))
        {
            throw new BudgetExceededException(tenant, BudgetDenialReason.RateLimitExceeded);
        }

        // 3. Hard spend quota over the rolling window. When already at/over quota,
        //    trip the breaker (open it for the cool-down) and refuse.
        if (_ledger.RollingSpend(tenant) >= _policy.SpendQuotaUsd)
        {
            _breakerOpenUntil[tenant] = now + _policy.BreakerCooldown;
            throw new BudgetExceededException(tenant, BudgetDenialReason.SpendQuotaExceeded);
        }
    }

    private void RecordSpend(string tenant, RagResponse response)
    {
        var cost = _costEstimator(response);
        if (cost <= 0)
        {
            return;
        }

        _ledger.Record(tenant, cost);
        // Keep the Ch 21 dashboard metric flowing — enforcement and observability
        // read the same spend figure.
        CostMeter.RecordCost(cost, tenant);
    }

    // Length-based fallback estimate: ~4 characters per token (a common rule of
    // thumb), priced at the output token rate. Deliberately rough — a host with
    // real UsageDetails supplies an exact estimator via the constructor.
    private static double EstimateCost(RagResponse response, TokenPricing pricing)
    {
        var approxOutputTokens = (response.Answer?.Length ?? 0) / 4.0;
        return approxOutputTokens * pricing.OutputPerToken;
    }
}
