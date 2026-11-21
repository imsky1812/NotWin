using NothingShell.Core;

namespace NothingShell.Core.Tests;

public sealed class FullscreenProbeThrottleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldProbe_AllowsFirst_ThenThrottlesWithin250ms()
    {
        var t = new FullscreenProbeThrottle(TimeSpan.FromMilliseconds(250));
        Assert.True(t.ShouldProbe(T0));
        Assert.False(t.ShouldProbe(T0.AddMilliseconds(100)));
        Assert.False(t.ShouldProbe(T0.AddMilliseconds(249)));
    }

    [Fact]
    public void ShouldProbe_AllowsAfterInterval()
    {
        var t = new FullscreenProbeThrottle(TimeSpan.FromMilliseconds(250));
        t.ShouldProbe(T0);
        Assert.True(t.ShouldProbe(T0.AddMilliseconds(250)));
        Assert.False(t.ShouldProbe(T0.AddMilliseconds(300)));
    }

    [Fact]
    public void SkippedProbe_ReportsTrailingDelay()
    {
        var t = new FullscreenProbeThrottle(TimeSpan.FromMilliseconds(250));
        t.ShouldProbe(T0);
        t.ShouldProbe(T0.AddMilliseconds(100));
        Assert.Equal(TimeSpan.FromMilliseconds(150), t.TrailingDelay(T0.AddMilliseconds(100)));
    }
}
