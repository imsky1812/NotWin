using System.Diagnostics;
using System.Windows.Threading;
using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Shell;

/// <summary>
/// Global mouse-move feed for the dock's edge trigger (WH_MOUSE_LL). Create, use and dispose on the WPF UI
/// thread: the hook is called through that thread's message loop.
///
/// The native callback only stores the point and schedules delivery, then returns at once (Windows silently
/// removes LL hooks that are slow). Delivery is throttled to 60 Hz: within a 1/60 s window only the latest
/// point is kept, and a trailing delivery makes sure the final resting point is always reported.
/// While <see cref="Paused"/> (full-screen app in front) the hook is uninstalled entirely, so games and
/// videos pay nothing for it.
/// </summary>
public sealed class MouseEdgeHook : IDisposable
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _trailing;
    private readonly Action _deliver;
    private MouseHookNative.Hook? _hook;
    private PointI _latest;
    private bool _pending;
    private long _lastDelivered;
    private bool _paused;
    private bool _disposed;
    private long _rawCount;
    private long _deliveredCount;
    private int _reinstallCount;

    /// <summary>Raised on the UI thread with the latest cursor point (physical pixels, clamped onto a monitor).</summary>
    public event Action<PointI>? Moved;

    public MouseEdgeHook(bool startPaused = false)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _deliver = Deliver;
        _trailing = new DispatcherTimer(DispatcherPriority.Input, _dispatcher);
        _trailing.Tick += (_, _) => Deliver();
        _paused = startPaused;
        if (!_paused) Install();
    }

    /// <summary>True while uninstalled (e.g. a full-screen app is in front).</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_disposed || value == _paused) return;
            _paused = value;
            if (value) Uninstall(); else Install();
        }
    }

    private void Install(bool log = true)
    {
        if (_hook is not null) return;
        try
        {
            _hook = new MouseHookNative.Hook(OnRawMove);
            if (log) Log.Info("mouse edge hook installed");
        }
        catch (Exception e)
        {
            Log.Error("mouse edge hook install failed", e);
        }
    }

    /// <summary>
    /// Uninstalls and installs the hook again (UI thread). Windows silently drops an LL hook the first time its
    /// thread misses LowLevelHooksTimeout; Explorer-related events (taskbar re-hidden, TaskbarCreated) are exactly
    /// when that can have happened, so those call this. Does nothing while paused: the hook is meant to be off then.
    /// </summary>
    public void Reinstall()
    {
        if (_disposed || _paused) return;
        _hook?.Dispose();
        _hook = null;
        Install(log: false);
        _reinstallCount++;
        if (_reinstallCount <= 5 || _reinstallCount % 50 == 0)
            Log.Info($"mouse edge hook reinstalled (count {_reinstallCount}, installed={_hook is not null})");
    }

    private void Uninstall()
    {
        _hook?.Dispose();
        _hook = null;
        _trailing.Stop();
        _pending = false;
        Log.Info("mouse edge hook removed");
    }

    // Runs inside the LL hook callback: copy the point, schedule, return.
    private void OnRawMove(int x, int y)
    {
        _rawCount++;
        _latest = new PointI(x, y);
        if (_pending) return;
        _pending = true;

        var since = Stopwatch.GetElapsedTime(_lastDelivered);
        if (since >= MinInterval)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Input, _deliver);
        }
        else
        {
            _trailing.Interval = MinInterval - since;
            _trailing.Start();
        }
    }

    private void Deliver()
    {
        _trailing.Stop();
        if (!_pending || _paused || _disposed) return;
        _pending = false;
        _lastDelivered = Stopwatch.GetTimestamp();
        _deliveredCount++;
        Moved?.Invoke(DockWindowNative.ClampToNearestMonitor(_latest));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hook?.Dispose();
        _hook = null;
        _trailing.Stop();
        Log.Info($"mouse edge hook stats: raw={_rawCount} delivered={_deliveredCount}");
    }
}
