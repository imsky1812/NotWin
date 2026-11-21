using System.Diagnostics;
using System.Windows.Threading;
using NotWin.Core.Timing;

namespace NotWin.Timing;

/// <summary>
/// The one shared timer (spec §6.6: notch tile, notch live state and, in Plan 5, the desktop widget). Ticks once a
/// second only while running, aimed at the next whole second of the countdown; at zero it raises
/// <see cref="Finished"/> once and flashes (6 toggles at 400 ms). The length is saved when − / + change it while idle.
/// </summary>
public sealed class TimerService : IDisposable
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _flash;
    private readonly Action<int>? _saveMinutes;
    private int _flashes;

    public TimerService(int minutes, Action<int>? saveMinutes = null)
    {
        Model = new TimerModel(minutes);
        _saveMinutes = saveMinutes;
        _tick = new DispatcherTimer(DispatcherPriority.Normal);
        _tick.Tick += (_, _) => OnTick();
        _flash = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(400) };
        _flash.Tick += (_, _) =>
        {
            FlashOn = !FlashOn;
            if (++_flashes >= 6) { _flash.Stop(); FlashOn = false; }
            Changed?.Invoke();
        };
    }

    public TimerModel Model { get; }
    public TimeSpan Now => _clock.Elapsed;
    public bool Running => Model.Phase == TimerPhase.Running;

    /// <summary>True during the red "on" half of the time-up flash.</summary>
    public bool FlashOn { get; private set; }

    public TimeSpan Remaining => Model.Remaining(Now);
    public double Fraction => Model.Fraction(Now);

    /// <summary>Remaining as mm:ss, rounded up (25:00 until the first second has passed).</summary>
    public string Text => Format(Remaining);

    /// <summary>Something visible changed (once a second while running, on every button, during the flash).</summary>
    public event Action? Changed;

    /// <summary>The countdown reached zero (once per run).</summary>
    public event Action? Finished;

    public static string Format(TimeSpan t)
    {
        var s = (int)Math.Ceiling(Math.Max(0, t.TotalSeconds) - 1e-9);
        return $"{s / 60:00}:{s % 60:00}";
    }

    public void Toggle()
    {
        StopFlash();
        Model.Toggle(Now);
        Arm();
        Changed?.Invoke();
    }

    public void Plus() => Change(+1);

    public void Minus() => Change(-1);

    private void Change(int sign)
    {
        StopFlash();
        var idle = Model.Phase is TimerPhase.Idle or TimerPhase.Finished;
        if (sign > 0) Model.Plus(Now); else Model.Minus(Now);
        if (idle) _saveMinutes?.Invoke(Model.Minutes);
        Arm();
        Changed?.Invoke();
    }

    private void OnTick()
    {
        _tick.Stop();
        if (Model.Tick(Now))
        {
            _flashes = 0;
            FlashOn = true;
            _flash.Start();
            Finished?.Invoke();
        }
        Arm();
        Changed?.Invoke();
    }

    // Next whole-second boundary of the remaining time, so the display changes exactly when the number does.
    private void Arm()
    {
        _tick.Stop();
        if (!Running) return;
        var frac = TimeSpan.FromTicks(Remaining.Ticks % Second.Ticks);
        _tick.Interval = (frac == TimeSpan.Zero ? Second : frac) + TimeSpan.FromMilliseconds(5);
        _tick.Start();
    }

    private void StopFlash()
    {
        _flash.Stop();
        FlashOn = false;
    }

    public void Dispose()
    {
        _tick.Stop();
        _flash.Stop();
    }
}
