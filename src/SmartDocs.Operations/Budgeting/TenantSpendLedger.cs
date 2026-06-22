namespace SmartDocs.Operations.Budgeting;

/// <summary>
/// A per-tenant rolling-window spend ledger (Ch 25 §"Budget enforcement"). The
/// Ch 21 <c>CostMeter</c> emits spend as an OpenTelemetry counter — perfect for a
/// dashboard, useless for a synchronous admission decision, because you cannot
/// read a counter back to ask "has tenant X spent more than its quota in the last
/// hour?". This ledger answers exactly that question: it keeps each tenant's
/// recent USD charges with their timestamps and reports the rolling total over a
/// trailing window, evicting charges that have aged out.
///
/// <para>
/// Time is read from an injected <see cref="TimeProvider"/> so the rolling window
/// can be tested deterministically. Thread-safe: a single lock guards the
/// per-tenant queues, which is ample for an admission-control hot path that does
/// O(charges-in-window) work per call.
/// </para>
/// </summary>
public sealed class TenantSpendLedger
{
    private readonly record struct Charge(long Ticks, double Usd);

    private readonly TimeSpan _window;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Queue<Charge>> _ledger = new(StringComparer.Ordinal);

    /// <summary>The trailing window over which spend is summed.</summary>
    public TimeSpan Window => _window;

    /// <summary>Create a ledger over a rolling <paramref name="window"/>.</summary>
    /// <param name="window">The trailing window spend is summed over. Must be positive.</param>
    /// <param name="timeProvider">The clock. Defaults to <see cref="TimeProvider.System"/>.</param>
    public TenantSpendLedger(TimeSpan window, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        _window = window;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Record a <paramref name="costUsd"/> charge against <paramref name="tenant"/>.</summary>
    /// <param name="tenant">The tenant the charge is attributed to.</param>
    /// <param name="costUsd">The charge in USD. Non-positive charges are ignored.</param>
    public void Record(string tenant, double costUsd)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        if (costUsd <= 0)
        {
            return;
        }

        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        lock (_gate)
        {
            if (!_ledger.TryGetValue(tenant, out var charges))
            {
                charges = new Queue<Charge>();
                _ledger[tenant] = charges;
            }

            Evict(charges, nowTicks);
            charges.Enqueue(new Charge(nowTicks, costUsd));
        }
    }

    /// <summary>The rolling spend for <paramref name="tenant"/> over the trailing window.</summary>
    /// <param name="tenant">The tenant to total.</param>
    /// <returns>The summed USD spend inside the window; 0 when nothing is recorded.</returns>
    public double RollingSpend(string tenant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);

        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        lock (_gate)
        {
            if (!_ledger.TryGetValue(tenant, out var charges))
            {
                return 0;
            }

            Evict(charges, nowTicks);

            double total = 0;
            foreach (var charge in charges)
            {
                total += charge.Usd;
            }
            return total;
        }
    }

    private void Evict(Queue<Charge> charges, long nowTicks)
    {
        var cutoff = nowTicks - _window.Ticks;
        while (charges.Count > 0 && charges.Peek().Ticks <= cutoff)
        {
            charges.Dequeue();
        }
    }
}
