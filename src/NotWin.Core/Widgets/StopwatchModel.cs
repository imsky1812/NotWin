using System.Globalization;

namespace NotWin.Core.Widgets;

/// <summary>
/// The stopwatch widget's state (Plan 13): in memory only, starting at zero each launch. Time comes from an injected
/// monotonic clock so pauses, resumes and the next-second schedule are testable.
/// </summary>
public sealed class StopwatchModel(Func<TimeSpan> clock)
{
    private TimeSpan _banked;      // elapsed before the current run
    private TimeSpan? _runningSince;

    public bool Running => _runningSince is not null;

    public TimeSpan Elapsed => _banked + (_runningSince is { } s ? clock() - s : TimeSpan.Zero);

    /// <summary>Time until the elapsed time reaches its next whole second (when the display next changes).</summary>
    public TimeSpan UntilNextSecond
    {
        get
        {
            var e = Elapsed;
            return TimeSpan.FromSeconds(Math.Floor(e.TotalSeconds) + 1) - e;
        }
    }

    public void Start() => _runningSince ??= clock();

    public void Pause()
    {
        if (_runningSince is not { } s) return;
        _banked += clock() - s;
        _runningSince = null;
    }

    /// <summary>Back to zero, only while paused (a running stopwatch is never reset by a stray click).</summary>
    public void Reset()
    {
        if (!Running) _banked = TimeSpan.Zero;
    }

    /// <summary>"MM:SS" under an hour, then "H:MM".</summary>
    public static string Format(TimeSpan t)
    {
        var whole = TimeSpan.FromSeconds(Math.Floor(t.TotalSeconds));
        return whole.TotalHours >= 1
            ? $"{(int)whole.TotalHours}:{whole.Minutes:00}"
            : string.Create(CultureInfo.InvariantCulture, $"{whole.Minutes:00}:{whole.Seconds:00}");
    }
}
