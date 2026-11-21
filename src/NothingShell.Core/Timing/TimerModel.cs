namespace NothingShell.Core.Timing;

public enum TimerPhase { Idle, Running, Paused, Finished }

/// <summary>
/// The one shared countdown (spec §6.4, §6.5): 25:00 by default, − / + by 1 minute (1-99 min). <c>now</c> is a
/// monotonic clock (the app passes Stopwatch time), so wall-clock changes never move it; after sleep it simply finishes
/// on the next tick, once. Pure: no timers inside.
/// </summary>
public sealed class TimerModel
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1), Max = TimeSpan.FromMinutes(99);
    private TimeSpan _remaining; // Idle / Paused / Finished
    private TimeSpan _endsAt;    // Running

    public TimerModel(int minutes)
    {
        Duration = Clamp(TimeSpan.FromMinutes(minutes));
        _remaining = Duration;
    }

    public TimerPhase Phase { get; private set; } = TimerPhase.Idle;
    public TimeSpan Duration { get; private set; }

    /// <summary>The length to save (whole minutes of <see cref="Duration"/>).</summary>
    public int Minutes => (int)Duration.TotalMinutes;

    public TimeSpan Remaining(TimeSpan now)
    {
        var r = Phase == TimerPhase.Running ? _endsAt - now : _remaining;
        return r < TimeSpan.Zero ? TimeSpan.Zero : r;
    }

    public double Fraction(TimeSpan now) =>
        Duration <= TimeSpan.Zero ? 0 : Math.Clamp(Remaining(now) / Duration, 0, 1);

    public void Toggle(TimeSpan now)
    {
        switch (Phase)
        {
            case TimerPhase.Idle:
            case TimerPhase.Paused:
                _endsAt = now + _remaining;
                Phase = TimerPhase.Running;
                break;
            case TimerPhase.Running:
                _remaining = Remaining(now);
                Phase = TimerPhase.Paused;
                break;
            case TimerPhase.Finished:
                _remaining = Duration;
                Phase = TimerPhase.Idle;
                break;
        }
    }

    public void Plus(TimeSpan now) => Change(now, +1);

    public void Minus(TimeSpan now) => Change(now, -1);

    /// <summary>True exactly once, when the countdown reaches zero (then <see cref="TimerPhase.Finished"/>).</summary>
    public bool Tick(TimeSpan now)
    {
        if (Phase != TimerPhase.Running || Remaining(now) > TimeSpan.Zero) return false;
        _remaining = TimeSpan.Zero;
        Phase = TimerPhase.Finished;
        return true;
    }

    private void Change(TimeSpan now, int sign)
    {
        if (Phase is TimerPhase.Idle or TimerPhase.Finished)
        {
            Duration = Clamp(Duration + sign * Minute);
            _remaining = Duration;
            Phase = TimerPhase.Idle;
            return;
        }
        var remaining = Remaining(now);
        if (sign < 0 && remaining < Minute) return;
        remaining = Clamp(remaining + sign * Minute);
        if (Phase == TimerPhase.Running) _endsAt = now + remaining;
        else _remaining = remaining;
        if (remaining > Duration) Duration = remaining;
    }

    private static TimeSpan Clamp(TimeSpan t) => t < Minute ? Minute : t > Max ? Max : t;
}
