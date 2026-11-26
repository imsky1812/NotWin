using System.Windows.Threading;
using Microsoft.Win32;
using NotWin.Core.Widgets;

namespace NotWin.Widgets;

/// <summary>
/// Screen time's sampler (Plan 13): app-wide, running whether or not the widget is on the desktop, so the week fills in.
/// Once a minute it records whether the session is unlocked and how long since the last input
/// (<see cref="ScreenTimeLog"/> decides what counts). Saves <c>screentime.json</c> at most every 5 minutes and on exit.
/// </summary>
public sealed class ScreenTimeTracker : IDisposable
{
    private static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(5);
    private readonly string _dataDir;
    private readonly Func<DateTime> _now;
    private readonly Func<TimeSpan> _idle;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private DateTime? _savedAt;
    private bool _dirty, _locked, _started, _disposed;

    public ScreenTimeTracker(string dataDir, Func<DateTime> now, Func<TimeSpan> idle)
    {
        _dataDir = dataDir;
        _now = now;
        _idle = idle;
        Log = new ScreenTimeLog(ScreenTimeStore.Load(dataDir));
        _timer.Tick += (_, _) => Tick();
    }

    public ScreenTimeLog Log { get; }

    /// <summary>Today's total changed (at most once a minute).</summary>
    public event Action? Changed;

    public DateTime Now => _now();

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        Tick();
        _timer.Start();
    }

    internal void Tick()
    {
        var now = _now();
        _savedAt ??= now;
        if (Log.Sample(now, !_locked, _idle()))
        {
            _dirty = true;
            Changed?.Invoke();
        }
        if (_dirty && now - _savedAt >= SaveEvery) Flush();
    }

    internal void OnSessionSwitch(bool locked)
    {
        Tick(); // credit the time up to the lock, then stop counting
        _locked = locked;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        bool? locked = e.Reason switch
        {
            SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect => true,
            SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect => false,
            _ => null,
        };
        if (locked is { } l) _timer.Dispatcher.BeginInvoke(() => OnSessionSwitch(l));
    }

    /// <summary>Writes the last 8 days if anything changed.</summary>
    public void Flush()
    {
        if (!_dirty) return;
        var now = _now();
        try
        {
            ScreenTimeStore.Save(_dataDir, Log.ToSave(now));
            _dirty = false;
            _savedAt = now;
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            NotWin.Log.Error("screen time save failed", e);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        if (_started) SystemEvents.SessionSwitch -= OnSessionSwitch;
        Flush();
    }
}
