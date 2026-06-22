using SmartDocs.Security.Input;

namespace SmartDocs.SecurityTests;

public sealed class RateLimiterTests
{
    [Fact]
    public void Allows_up_to_limit_then_blocks()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var limiter = new RateLimiter(maxRequests: 3, window: TimeSpan.FromMinutes(1), time);

        Assert.True(limiter.TryAcquire("tenant-A", "user-1"));
        Assert.True(limiter.TryAcquire("tenant-A", "user-1"));
        Assert.True(limiter.TryAcquire("tenant-A", "user-1"));
        Assert.False(limiter.TryAcquire("tenant-A", "user-1")); // 4th in window
    }

    [Fact]
    public void Allows_again_after_window_elapses()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var limiter = new RateLimiter(maxRequests: 2, window: TimeSpan.FromMinutes(1), time);

        Assert.True(limiter.TryAcquire("t", "u"));
        Assert.True(limiter.TryAcquire("t", "u"));
        Assert.False(limiter.TryAcquire("t", "u"));

        time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

        Assert.True(limiter.TryAcquire("t", "u"));
    }

    [Fact]
    public void Limits_are_per_principal()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var limiter = new RateLimiter(maxRequests: 1, window: TimeSpan.FromMinutes(1), time);

        Assert.True(limiter.TryAcquire("tenant-A", "user-1"));
        Assert.False(limiter.TryAcquire("tenant-A", "user-1"));
        // Different user and different tenant each get their own budget.
        Assert.True(limiter.TryAcquire("tenant-A", "user-2"));
        Assert.True(limiter.TryAcquire("tenant-B", "user-1"));
    }
}
