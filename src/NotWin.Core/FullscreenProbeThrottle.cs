namespace NotWin.Core;

/// <summary>
/// Limits full-screen probes caused by the foreground window moving (EVENT_OBJECT_LOCATIONCHANGE fires for every
/// pixel of a drag). The first probe runs at once; probes within <c>min</c> of the last are skipped, and the
/// caller schedules one trailing probe after <see cref="TrailingDelay"/> so the final state is never missed.
/// </summary>
public sealed class FullscreenProbeThrottle(TimeSpan min)
{
    private DateTimeOffset? _last;

    public bool ShouldProbe(DateTimeOffset now)
    {
        if (_last is { } last && now - last < min) return false;
        _last = now;
        return true;
    }

    /// <summary>Time until the next probe is allowed (zero if allowed now).</summary>
    public TimeSpan TrailingDelay(DateTimeOffset now)
    {
        if (_last is not { } last) return TimeSpan.Zero;
        var wait = min - (now - last);
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }
}
