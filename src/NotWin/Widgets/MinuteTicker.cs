using System.Windows.Threading;

namespace NotWin.Widgets;

/// <summary>
/// Calls an action just after every minute change (50 ms late so the new minute has begun), rescheduling itself from
/// the clock each time so it never drifts. Used by the clock widgets (Plan 13 pulled it out of <see cref="ClockWidget"/>).
/// </summary>
public sealed class MinuteTicker : IDisposable
{
    private readonly DispatcherTimer _timer = new();
    private readonly Action _tick;

    public MinuteTicker(Action tick)
    {
        _tick = tick;
        _timer.Tick += (_, _) =>
        {
            Schedule();
            _tick();
        };
    }

    public bool Running => _timer.IsEnabled;

    /// <summary>Starts (or restarts) the schedule; the action first runs at the next minute change.</summary>
    public void Start() => Schedule();

    public void Stop() => _timer.Stop();

    private void Schedule()
    {
        var now = DateTime.Now;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();
}
