using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;
using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Shell;

/// <summary>
/// Global mouse-move feed for the dock's edge trigger (WH_MOUSE_LL). Create, use and dispose on the WPF UI
/// thread; the hook itself lives on the shared <see cref="HookThread"/>, so a busy UI thread can never make
/// Windows drop it.
///
/// The native callback (hook thread) only stores the point and posts one delivery request, then returns at once.
/// Delivery runs on the UI thread, throttled to 60 Hz: within a 1/60 s window only the latest point is kept,
/// and a trailing delivery makes sure the final resting point is always reported.
/// While <see cref="Paused"/> (full-screen app in front) the hook is uninstalled entirely, so games and
/// videos pay nothing for it.
/// </summary>
public sealed class MouseEdgeHook : IDisposable
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private readonly Dispatcher _dispatcher;
    private readonly HookThread _thread;
    private readonly DispatcherTimer _trailing;
    private readonly Action _schedule;
    private MouseHookNative.Hook? _hook;
    private long _latestPacked; // x in the high 32 bits, y in the low; written on the hook thread
    private int _pending;        // 1 while a delivery is queued (hook thread sets, UI thread clears)
    private long _lastDelivered;
    private bool _paused;
    private bool _disposed;
    private long _rawCount;
    private long _deliveredCount;
    private int _reinstallCount;

    /// <summary>Raised on the UI thread with the latest cursor point (physical pixels, clamped onto a monitor).</summary>
    public event Action<PointI>? Moved;

    public MouseEdgeHook(HookThread thread, bool startPaused = false)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _thread = thread;
        _schedule = ScheduleOnUi;
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
            _hook = new MouseHookNative.Hook(_thread, OnRawMove);
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
        Interlocked.Exchange(ref _pending, 0);
        Log.Info("mouse edge hook removed");
    }

    // Hook thread, inside the LL callback: store the point, post one request, return.
    private void OnRawMove(int x, int y)
    {
        Interlocked.Increment(ref _rawCount);
        Interlocked.Exchange(ref _latestPacked, ((long)x << 32) | (uint)y);
        if (Interlocked.Exchange(ref _pending, 1) == 1) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Input, _schedule);
    }

    // UI thread: deliver now, or after the rest of the 1/60 s window.
    private void ScheduleOnUi()
    {
        if (_paused || _disposed) { Interlocked.Exchange(ref _pending, 0); return; }
        var since = Stopwatch.GetElapsedTime(_lastDelivered);
        if (since >= MinInterval) Deliver();
        else
        {
            _trailing.Interval = MinInterval - since;
            _trailing.Start();
        }
    }

    private void Deliver()
    {
        _trailing.Stop();
        if (_paused || _disposed) { Interlocked.Exchange(ref _pending, 0); return; }
        // Clear before reading: a move that lands after this posts a fresh request, so nothing is lost.
        if (Interlocked.Exchange(ref _pending, 0) == 0) return;
        var packed = Interlocked.Read(ref _latestPacked);
        _lastDelivered = Stopwatch.GetTimestamp();
        _deliveredCount++;
        Moved?.Invoke(DockWindowNative.ClampToNearestMonitor(new PointI((int)(packed >> 32), (int)packed)));
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
