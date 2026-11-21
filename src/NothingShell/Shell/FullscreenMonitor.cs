using System.Threading;
using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Shell;

/// <summary>
/// Tracks whether a full-screen app (game, video) is in front. Must be created and disposed on the WPF UI
/// thread: WinEvent out-of-context hooks are delivered through that thread's message loop.
/// Monitor choice: the probe uses the monitor the foreground window is on; v1 consumers only act on the
/// primary monitor, so callers should treat the flag as "something is full-screen" for the primary dock.
/// Own-process windows are skipped by the hook (WINEVENT_SKIPOWNPROCESS), so the dock never triggers itself.
/// </summary>
public sealed class FullscreenMonitor : IDisposable
{
    private readonly FullscreenNative.Hook _hook; // field keeps the delegate alive
    private nint _foreground;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private System.Threading.Timer? _recheck;
    private System.Threading.Timer? _trailing;
    private bool _trailingPending;
    private readonly FullscreenProbeThrottle _throttle = new(TimeSpan.FromMilliseconds(250));
    private bool _disposed;

    public bool IsFullscreen { get; private set; }

    /// <summary>Raised only on transitions, on the UI thread.</summary>
    public event Action<bool>? Changed;

    /// <summary>A maximized app is in front on the primary (dock) monitor (user, 2026-11-21).</summary>
    public bool IsMaximizedFront { get; private set; }

    /// <summary>Raised only on transitions of <see cref="IsMaximizedFront"/>, on the UI thread.</summary>
    public event Action<bool>? MaximizedChanged;

    public FullscreenMonitor()
    {
        _foreground = FullscreenNative.GetForeground();
        Evaluate();
        _hook = new FullscreenNative.Hook(OnEvent);
        // Win+L / sleep: the lock screen is a full-screen app the shell's raised windows would otherwise sit on top of.
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private bool _locked;

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        bool? locked = e.Reason switch
        {
            Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect
                or Microsoft.Win32.SessionSwitchReason.RemoteDisconnect => true,
            Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect
                or Microsoft.Win32.SessionSwitchReason.RemoteConnect => false,
            _ => null,
        };
        if (locked is not { } l) return;
        void Apply()
        {
            if (_disposed || _locked == l) return;
            _locked = l;
            Log.Info(l ? "session locked: shell hidden" : "session unlocked");
            Evaluate();
            if (!l) ScheduleRecheck(); // the desktop settles a moment after unlocking
        }
        if (_ui is not null) _ui.Post(_ => Apply(), null); // SystemEvents raises on its own thread
        else Apply();
    }

    private void OnEvent(uint eventId, nint hwnd, int idObject)
    {
        if (_disposed) return;
        if (eventId == FullscreenNative.ForegroundEvent)
        {
            _foreground = hwnd;
            // SHQueryUserNotificationState lags a moment behind a closing full-screen window, and nothing else
            // fires if the new foreground window never moves, so re-check once after it settles.
            ScheduleRecheck();
        }
        else if (eventId == FullscreenNative.LocationChangeEvent)
        {
            // Very chatty: only the foreground window's own frame matters, and at most one probe per 250 ms
            // (a trailing probe catches the final position of a drag or a resize animation).
            if (idObject != FullscreenNative.WindowObjectId || hwnd != _foreground) return;
            var now = DateTimeOffset.UtcNow;
            if (!_throttle.ShouldProbe(now))
            {
                ScheduleTrailing(_throttle.TrailingDelay(now));
                return;
            }
        }
        else return;
        Evaluate();
    }

    private void ScheduleTrailing(TimeSpan delay)
    {
        if (_trailingPending) return;
        _trailingPending = true;
        _trailing?.Dispose();
        _trailing = new System.Threading.Timer(_ =>
        {
            if (_disposed || _ui is null) return;
            _ui.Post(__ =>
            {
                _trailingPending = false;
                if (_disposed) return;
                _throttle.ShouldProbe(DateTimeOffset.UtcNow); // record this probe; the trailing one always runs
                Evaluate();
            }, null);
        }, null, (int)Math.Ceiling(delay.TotalMilliseconds) + 1, Timeout.Infinite);
    }

    private void ScheduleRecheck()
    {
        _recheck?.Dispose();
        _recheck = new System.Threading.Timer(_ =>
        {
            if (_disposed) return;
            if (_ui is not null) _ui.Post(__ => { if (!_disposed) Evaluate(); }, null);
        }, null, 500, Timeout.Infinite);
    }

    private void Evaluate()
    {
        var probe = FullscreenNative.Probe();
        var own = FullscreenNative.IsForegroundOwnProcess();
        var maximized = FullscreenRule.DecideMaximized(probe, Native.DockWindowNative.GetPrimaryMonitor().Bounds, IsMaximizedFront, own);
        if (maximized != IsMaximizedFront)
        {
            IsMaximizedFront = maximized;
            Log.Info($"maximized app in front={maximized}");
            MaximizedChanged?.Invoke(maximized);
        }
        var now = FullscreenRule.Decide(probe, IsFullscreen, own, _locked);
        // What decided it (for the next time detection looks wrong): class, maximized, frame, QUNS, geometry.
        if (now != IsFullscreen && probe is { } p)
            Log.Info($"fullscreen probe: class={p.ClassName} maximized={p.IsMaximized} hasFrame={p.HasFrame} quns={p.QunsSaysFullscreen} " +
                     $"frame={p.Foreground} monitor={p.Monitor}");
        if (now == IsFullscreen) return;
        IsFullscreen = now;
        Changed?.Invoke(now);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        _hook.Dispose();
        _recheck?.Dispose();
        _trailing?.Dispose();
    }
}
