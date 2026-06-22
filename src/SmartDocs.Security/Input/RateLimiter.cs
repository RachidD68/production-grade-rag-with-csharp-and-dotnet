namespace SmartDocs.Security.Input;

/// <summary>
/// Per-principal sliding-window rate limiter keyed on
/// <c>(tenantId, userId)</c>. A principal may make at most
/// <see cref="MaxRequests"/> calls within any <see cref="Window"/>; the N+1th
/// call inside the window is rejected. Time is read from an injected
/// <see cref="TimeProvider"/> so tests can advance the clock deterministically.
/// Thread-safe.
/// </summary>
public sealed class RateLimiter
{
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Tenant, string User), Queue<long>> _hits = [];

    /// <summary>Maximum requests permitted per principal per window.</summary>
    public int MaxRequests => _maxRequests;

    /// <summary>Length of the sliding window.</summary>
    public TimeSpan Window => _window;

    public RateLimiter(int maxRequests, TimeSpan window, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRequests);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        _maxRequests = maxRequests;
        _window = window;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Attempts to admit one request for <c>(tenantId, userId)</c>. Returns true
    /// and records the hit when under the limit; returns false (recording
    /// nothing) when the window is full.
    /// </summary>
    public bool TryAcquire(string tenantId, string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(userId);

        var nowTicks = _timeProvider.GetUtcNow().UtcTicks;
        var cutoff = nowTicks - _window.Ticks;
        var key = (tenantId, userId);

        lock (_gate)
        {
            if (!_hits.TryGetValue(key, out var timestamps))
            {
                timestamps = new Queue<long>();
                _hits[key] = timestamps;
            }

            // Evict timestamps that fell out of the trailing window.
            while (timestamps.Count > 0 && timestamps.Peek() <= cutoff)
            {
                timestamps.Dequeue();
            }

            if (timestamps.Count >= _maxRequests)
            {
                return false;
            }

            timestamps.Enqueue(nowTicks);
            return true;
        }
    }
}
