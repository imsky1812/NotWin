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

    public FullscreenMonitor()
    {
        _foreground = FullscreenNative.GetForeground();
        Evaluate();
        _hook = new FullscreenNative.Hook(OnEvent);
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
        var now = FullscreenRule.Decide(FullscreenNative.Probe(), IsFullscreen, FullscreenNative.IsForegroundOwnProcess());
        if (now == IsFullscreen) return;
        IsFullscreen = now;
        Changed?.Invoke(now);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hook.Dispose();
        _recheck?.Dispose();
        _trailing?.Dispose();
    }
}
