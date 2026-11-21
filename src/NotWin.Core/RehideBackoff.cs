namespace NotWin.Core;

public enum RehideDecision
{
    /// <summary>Hide the taskbar now.</summary>
    Proceed,
    /// <summary>Too many re-hides in a short time: the pause starts now (log this once).</summary>
    PauseStarted,
    /// <summary>Inside a pause that already started: do nothing.</summary>
    Paused,
}

/// <summary>
/// Backoff for the "Explorer showed the taskbar again" hook. If Explorer re-shows the bar every time it is hidden,
/// both processes would spin: more than <c>maxInWindow</c> re-hides within <c>window</c> stops re-hiding for
/// <c>pause</c>. Pure and clock-free: callers pass a monotonic timestamp.
/// </summary>
public sealed class RehideBackoff
{
    private readonly int _maxInWindow;
    private readonly TimeSpan _window;
    private readonly TimeSpan _pause;
    private readonly Queue<TimeSpan> _recent = new();
    private TimeSpan _pausedUntil;
    private bool _paused;

    public RehideBackoff(int maxInWindow = 20, TimeSpan? window = null, TimeSpan? pause = null)
    {
        _maxInWindow = maxInWindow;
        _window = window ?? TimeSpan.FromSeconds(2);
        _pause = pause ?? TimeSpan.FromSeconds(5);
    }

    public TimeSpan PauseLength => _pause;

    /// <summary>Call once per intended re-hide. <see cref="RehideDecision.Proceed"/> records it.</summary>
    public RehideDecision Next(TimeSpan now)
    {
        if (_paused)
        {
            if (now < _pausedUntil) return RehideDecision.Paused;
            _paused = false;
            _recent.Clear(); // the pause is over: one fresh check, then count again from zero
        }

        while (_recent.Count > 0 && now - _recent.Peek() > _window) _recent.Dequeue();

        if (_recent.Count >= _maxInWindow)
        {
            _paused = true;
            _pausedUntil = now + _pause;
            _recent.Clear();
            return RehideDecision.PauseStarted;
        }

        _recent.Enqueue(now);
        return RehideDecision.Proceed;
    }
}
