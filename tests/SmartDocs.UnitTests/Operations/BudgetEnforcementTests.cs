using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using SmartDocs.Generation;
using SmartDocs.Operations.Budgeting;

namespace SmartDocs.UnitTests.Operations;

public sealed class BudgetEnforcementTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static RagResponse Answer(string text = "the answer") =>
        new(text, [], LatencyMs: 1, Strategy: "stub");

    // --- TenantSpendLedger ---------------------------------------------------

    [Fact]
    public void Ledger_sums_charges_inside_the_window()
    {
        var ledger = new TenantSpendLedger(TimeSpan.FromHours(1), new MutableTimeProvider(Start));

        ledger.Record("t1", 1.50);
        ledger.Record("t1", 2.25);
        ledger.Record("t2", 9.99);

        Assert.Equal(3.75, ledger.RollingSpend("t1"), precision: 6);
        Assert.Equal(9.99, ledger.RollingSpend("t2"), precision: 6);
    }

    [Fact]
    public void Ledger_evicts_charges_older_than_the_window()
    {
        var clock = new MutableTimeProvider(Start);
        var ledger = new TenantSpendLedger(TimeSpan.FromMinutes(10), clock);

        ledger.Record("t1", 5.0);
        clock.Advance(TimeSpan.FromMinutes(11)); // first charge ages out

        Assert.Equal(0, ledger.RollingSpend("t1"), precision: 6);

        ledger.Record("t1", 2.0);
        Assert.Equal(2.0, ledger.RollingSpend("t1"), precision: 6);
    }

    [Fact]
    public void Ledger_ignores_non_positive_charges()
    {
        var ledger = new TenantSpendLedger(TimeSpan.FromHours(1), new MutableTimeProvider(Start));

        ledger.Record("t1", 0);
        ledger.Record("t1", -3);

        Assert.Equal(0, ledger.RollingSpend("t1"), precision: 6);
    }

    // --- BudgetEnforcingPipeline: spend quota --------------------------------

    [Fact]
    public async Task Under_quota_requests_pass_through()
    {
        var inner = new CountingPipeline(_ => Answer());
        var ledger = new TenantSpendLedger(TimeSpan.FromHours(1), new MutableTimeProvider(Start));
        var pipeline = new BudgetEnforcingPipeline(
            inner,
            BudgetPolicy.Default with { SpendQuotaUsd = 100 },
            ledger,
            timeProvider: new MutableTimeProvider(Start));

        var response = await pipeline.AskAsync("hello");

        Assert.Equal("the answer", response.Answer);
        Assert.Equal(1, inner.AskCalls);
    }

    [Fact]
    public async Task Request_is_blocked_once_rolling_spend_reaches_quota()
    {
        var clock = new MutableTimeProvider(Start);
        var inner = new CountingPipeline(_ => Answer());
        var ledger = new TenantSpendLedger(TimeSpan.FromHours(1), clock);

        // Pre-load the tenant over its tiny $1 quota.
        ledger.Record("default", 1.50);

        var pipeline = new BudgetEnforcingPipeline(
            inner,
            BudgetPolicy.Default with { SpendQuotaUsd = 1.0, MaxRequests = 1000 },
            ledger,
            timeProvider: clock);

        var ex = await Assert.ThrowsAsync<BudgetExceededException>(() => pipeline.AskAsync("hi"));

        Assert.Equal(BudgetDenialReason.SpendQuotaExceeded, ex.Reason);
        Assert.Equal("default", ex.Tenant);
        Assert.Equal(0, inner.AskCalls); // blocked before the inner pipeline ran
    }

    [Fact]
    public async Task Breaker_stays_open_for_the_cooldown_then_rearms()
    {
        var clock = new MutableTimeProvider(Start);
        var inner = new CountingPipeline(_ => Answer());
        var ledger = new TenantSpendLedger(TimeSpan.FromHours(1), clock);
        ledger.Record("default", 10.0);

        var pipeline = new BudgetEnforcingPipeline(
            inner,
            BudgetPolicy.Default with
            {
                SpendQuotaUsd = 1.0,
                MaxRequests = 1000,
                BreakerCooldown = TimeSpan.FromMinutes(5),
            },
            ledger,
            timeProvider: clock);

        // First over-budget call trips the breaker (SpendQuotaExceeded).
        var first = await Assert.ThrowsAsync<BudgetExceededException>(() => pipeline.AskAsync("a"));
        Assert.Equal(BudgetDenialReason.SpendQuotaExceeded, first.Reason);

        // Inside the cool-down the breaker is open — fast refusal.
        clock.Advance(TimeSpan.FromMinutes(2));
        var second = await Assert.ThrowsAsync<BudgetExceededException>(() => pipeline.AskAsync("b"));
        Assert.Equal(BudgetDenialReason.CircuitOpen, second.Reason);

        // After the cool-down the breaker re-arms; spend is still over quota so it
        // trips again rather than admitting — but via the quota path, proving the
        // breaker actually reset.
        clock.Advance(TimeSpan.FromMinutes(4)); // total 6 min > 5 min cool-down
        var third = await Assert.ThrowsAsync<BudgetExceededException>(() => pipeline.AskAsync("c"));
        Assert.Equal(BudgetDenialReason.SpendQuotaExceeded, third.Reason);
    }

    // --- BudgetEnforcingPipeline: rate ceiling -------------------------------

    [Fact]
    public async Task Rate_ceiling_blocks_a_burst()
    {
        var clock = new MutableTimeProvider(Start);
        var inner = new CountingPipeline(_ => Answer(""));   // empty answer => ~0 cost
        var ledger = new TenantSpendLedger(TimeSpan.FromHours(1), clock);

        var pipeline = new BudgetEnforcingPipeline(
            inner,
            BudgetPolicy.Default with { SpendQuotaUsd = 1000, MaxRequests = 2, RateWindow = TimeSpan.FromMinutes(1) },
            ledger,
            timeProvider: clock);

        await pipeline.AskAsync("1");
        await pipeline.AskAsync("2");

        var ex = await Assert.ThrowsAsync<BudgetExceededException>(() => pipeline.AskAsync("3"));
        Assert.Equal(BudgetDenialReason.RateLimitExceeded, ex.Reason);
        Assert.Equal(2, inner.AskCalls);
    }

    [Fact]
    public async Task Successful_calls_accumulate_spend_in_the_ledger()
    {
        var clock = new MutableTimeProvider(Start);
        var inner = new CountingPipeline(_ => Answer(new string('x', 4000))); // ~1000 tokens
        var ledger = new TenantSpendLedger(TimeSpan.FromHours(1), clock);

        var pipeline = new BudgetEnforcingPipeline(
            inner,
            BudgetPolicy.Default with { SpendQuotaUsd = 1000, MaxRequests = 1000 },
            ledger,
            timeProvider: clock);

        await pipeline.AskAsync("q");

        // The length-based estimator priced a non-zero cost into the ledger.
        Assert.True(ledger.RollingSpend("default") > 0);
    }

    [Fact]
    public async Task Streaming_path_is_blocked_when_over_quota()
    {
        var clock = new MutableTimeProvider(Start);
        var inner = new CountingPipeline(_ => Answer());
        var ledger = new TenantSpendLedger(TimeSpan.FromHours(1), clock);
        ledger.Record("default", 5.0);

        var pipeline = new BudgetEnforcingPipeline(
            inner,
            BudgetPolicy.Default with { SpendQuotaUsd = 1.0, MaxRequests = 1000 },
            ledger,
            timeProvider: clock);

        await Assert.ThrowsAsync<BudgetExceededException>(async () =>
        {
            await foreach (var _ in pipeline.AskStreamingAsync("hi"))
            {
            }
        });

        Assert.Equal(0, inner.StreamCalls);
    }

    // --- DI registration -----------------------------------------------------

    [Fact]
    public async Task AddBudgetEnforcement_wraps_the_registered_pipeline()
    {
        var services = new ServiceCollection();
        var inner = new CountingPipeline(_ => Answer());
        services.AddSingleton<IRagPipeline>(inner);
        services.AddSingleton<TimeProvider>(new MutableTimeProvider(Start));
        // A quota smaller than one answer's estimated cost: the first call is
        // admitted (ledger empty) and records spend; that pushes the rolling total
        // past the quota so the SECOND call is blocked.
        services.AddBudgetEnforcement(
            BudgetPolicy.Default with { SpendQuotaUsd = 0.0000005, MaxRequests = 1000 });

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IRagPipeline>();

        Assert.IsType<BudgetEnforcingPipeline>(pipeline);

        await pipeline.AskAsync("first");
        await Assert.ThrowsAsync<BudgetExceededException>(() => pipeline.AskAsync("second"));
    }

    private sealed class CountingPipeline : IRagPipeline
    {
        private readonly Func<string, RagResponse> _ask;
        public int AskCalls { get; private set; }
        public int StreamCalls { get; private set; }

        public CountingPipeline(Func<string, RagResponse> ask) => _ask = ask;

        public Task<RagResponse> AskAsync(string question, CancellationToken ct = default)
        {
            AskCalls++;
            return Task.FromResult(_ask(question));
        }

        public async IAsyncEnumerable<RagStreamEvent> AskStreamingAsync(
            string question,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            StreamCalls++;
            await Task.Yield();
            yield return new RagStreamEvent(RagStreamEventKind.Sources, Sources: []);
            yield return new RagStreamEvent(RagStreamEventKind.Token, Token: _ask(question).Answer);
            yield return new RagStreamEvent(RagStreamEventKind.Done);
        }
    }
}
