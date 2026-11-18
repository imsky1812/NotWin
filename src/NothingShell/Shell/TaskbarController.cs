using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Shell;

/// <summary>
/// Hides the Windows taskbar for the lifetime of the shell and always gives it back.
/// Dispose is idempotent and thread-safe; it may be called from Exit, SessionEnding and crash handlers.
/// </summary>
public sealed class TaskbarController : IDisposable
{
    // TaskbarCreated is broadcast (HWND_BROADCAST) to top-level windows only; a message-only window
    // (HWND_MESSAGE parent) does not receive it. So this is a hidden, never-shown top-level tool window.
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;

    private readonly object _gate = new();
    private readonly string _stateDir;
    private readonly TaskbarRestoreStore _store;
    private TaskbarRestoreState? _original;
    private Process? _watchdog;
    private HwndSource? _source;
    private uint _taskbarCreated;
    private bool _started;
    private bool _restored;
    private uint _quitMessage;
    private bool _watchdogRelaunched;

    /// <summary>Registered window message name broadcast by <c>NothingShell.exe --quit</c>.</summary>
    public const string QuitMessageName = "NothingShell.Quit";

    /// <summary>Raised on the UI thread when another process asked the shell to quit (<c>--quit</c>).</summary>
    public event Action? QuitRequested;
    private Dispatcher? _dispatcher;
    private TaskbarShowHook? _showHook;
    private bool _rehideQueued;
    private int _rehideCount;
    private readonly RehideBackoff _backoff = new();
    private DispatcherTimer? _resumeTimer;
    // Set first thing in Dispose: background auto-hide work must not re-enable auto-hide after the taskbar is given back.
    private volatile bool _stopping;
    private readonly object _autoHideGate = new();

    public TaskbarController(string? stateDir = null)
    {
        _stateDir = stateDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NothingShell");
        _store = new TaskbarRestoreStore(_stateDir);
    }

    /// <summary>Raised on the UI thread after Explorer restarted and the taskbar was hidden again.</summary>
    public event Action? TaskbarRecreated;

    /// <summary>Raised on the UI thread after the show hook hid the taskbar again (Explorer was busy: a good moment to renew UI-thread hooks).</summary>
    public event Action? TaskbarReHidden;

    /// <summary>Set by the TaskbarRecreated handler: whether the appbar was registered again (false when keep-visible is off).</summary>
    public bool AppBarReRegistered { get; set; }

    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;

            var leftover = _store.TryLoad(out var s) ? s : null;
            _original = TaskbarSession.ResolveOriginal(leftover, TaskbarNative.GetAutoHide());
            Log.Info($"taskbar original autoHide={_original.WasAutoHide} (leftover={leftover is not null})");
            _store.Save(_original);

            // The watchdog is the safety net for hard kills: never hide the taskbar without a live one.
            try { _watchdog = WatchdogLauncher.Start(Environment.ProcessId, _stateDir); }
            catch (Exception e) { Log.Error("failed to start watchdog", e); }
            var alive = false;
            try { alive = _watchdog is not null && !_watchdog.WaitForExit(300); }
            catch (Exception e) { Log.Error("watchdog check failed", e); }
            if (!alive)
            {
#if DEBUG
                Log.Info("WARNING: watchdog not running; hiding taskbar anyway (Debug only).");
#else
                Log.Error("watchdog is not running; refusing to hide the taskbar");
                try { _watchdog?.Dispose(); } catch (Exception) { }
                _watchdog = null;
                try
                {
                    // A leftover state means a previous run may have left the taskbar hidden: put it back first.
                    if (leftover is not null) TaskbarNative.Restore(_original);
                    _store.Delete();
                }
                catch (Exception e) { Log.Error("cleanup after watchdog refusal failed", e); }
                _restored = true; // nothing was hidden; Dispose has nothing to do
                return;
#endif
            }

            ApplyHidden();

            _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            var p = new HwndSourceParameters("NothingShellTaskbarWatcher")
            {
                WindowStyle = WsPopup,
                ExtendedWindowStyle = WsExToolWindow,
                Width = 0,
                Height = 0,
            };
            _source = new HwndSource(p);
            _source.AddHook(WndProc);
            // A uiAccess process filters registered messages from the (lower-level) Explorer under UIPI: let TaskbarCreated in.
            var ok = AppBarNative.AllowMessage(_source.Handle, _taskbarCreated, out var err);
            Log.Info(ok ? "taskbar watcher: TaskbarCreated allowed through UIPI" : $"taskbar watcher: ChangeWindowMessageFilterEx failed (error {err})");
            // The same hidden top-level window receives `NothingShell.exe --quit` (a broadcast registered message).
            _quitMessage = RegisterWindowMessage(QuitMessageName);
            AppBarNative.AllowMessage(_source.Handle, _quitMessage, out _);

            if (_watchdog is not null)
            {
                _watchdog.EnableRaisingEvents = true;
                _watchdog.Exited += OnWatchdogExited;
            }

            // Explorer shows its taskbar again by itself when a full-screen window closes: hide it again.
            _dispatcher = Dispatcher.CurrentDispatcher;
            _showHook = new TaskbarShowHook(OnTaskbarShown);
            if (!_showHook.Installed) Log.Error("taskbar show hook failed to install", null);
        }
    }

    // Runs inside the WinEvent callback: only queue the work (ShowWindow on Explorer's window could block here).
    private void OnTaskbarShown()
    {
        if (_rehideQueued) return;
        _rehideQueued = true;
        _dispatcher?.BeginInvoke(DispatcherPriority.Background, ReHideAfterShow);
    }

    private void ReHideAfterShow()
    {
        _rehideQueued = false;
        var hidden = false;
        lock (_gate)
        {
            if (_showHook is null || _restored) return; // shutting down / taskbar given back on purpose
            try
            {
                // Any explorer tray window, so a re-shown secondary-monitor bar is hidden too.
                if (!TaskbarNative.IsAnyVisible()) return;

                // If Explorer re-shows the bar every time it is hidden, stop fighting it for a while.
                switch (_backoff.Next(TimeSpan.FromMilliseconds(Environment.TickCount64)))
                {
                    case RehideDecision.Paused:
                        return;
                    case RehideDecision.PauseStarted:
                        Log.Info($"taskbar re-shown too often: not re-hiding for {_backoff.PauseLength.TotalSeconds:F0} s");
                        // One check afterwards: the bar may be visible by then and no further show event may come.
                        _resumeTimer ??= new DispatcherTimer(DispatcherPriority.Background, _dispatcher!)
                        {
                            Interval = _backoff.PauseLength + TimeSpan.FromMilliseconds(100),
                        };
                        _resumeTimer.Tick -= OnResumeTick;
                        _resumeTimer.Tick += OnResumeTick;
                        _resumeTimer.Start();
                        return;
                }

                TaskbarNative.HideAll();
                hidden = true;
                _rehideCount++;
                if (_rehideCount <= 5 || _rehideCount % 50 == 0)
                    Log.Info($"taskbar shown again by Explorer -> hidden again (count {_rehideCount})");
            }
            catch (Exception e) { Log.Error("re-hide after show failed", e); }
        }

        if (!hidden) return;
        EnsureAutoHideInBackground();
        // Outside _gate: handlers touch other components.
        try { TaskbarReHidden?.Invoke(); } catch (Exception e) { Log.Error("TaskbarReHidden handler failed", e); }
    }

    private void OnResumeTick(object? sender, EventArgs e)
    {
        _resumeTimer?.Stop();
        ReHideAfterShow();
    }

    /// <summary>
    /// Re-applies auto-hide if it got turned off (otherwise the work area stays reserved and leaves a dead strip).
    /// SHAppBarMessage calls into Explorer synchronously, so this runs on a pool thread, never on the UI thread
    /// that hosts the mouse hook.
    /// </summary>
    private void EnsureAutoHideInBackground()
    {
        if (_stopping) return;
        Task.Run(() =>
        {
            lock (_autoHideGate)
            {
                if (_stopping || _restored) return;
                try
                {
                    if (TaskbarNative.GetAutoHide()) return;
                    TaskbarNative.SetAutoHide(true);
                    Log.Info("taskbar auto-hide had been turned off -> applied again");
                }
                catch (Exception e) { Log.Error("re-applying auto-hide failed", e); }
            }
        });
    }

    // Thread-pool thread. The watchdog is the safety net for hard kills; if it dies while we run (killed in Task
    // Manager, crashed), start it once more. If that fails the shell keeps running: the taskbar stays hidden by
    // design and the user can still quit from the menu, which restores it.
    private void OnWatchdogExited(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_stopping || _restored || !ReferenceEquals(sender, _watchdog)) return;
            int code;
            try { code = _watchdog!.ExitCode; } catch { code = int.MinValue; }
            Log.Info($"watchdog exited (code {code})");
            if (_watchdogRelaunched)
            {
                Log.Error("watchdog exited again; not relaunching (quit from the menu restores the taskbar)");
                return;
            }
            _watchdogRelaunched = true;
            try { _watchdog?.Dispose(); } catch { }
            _watchdog = WatchdogLauncher.Start(Environment.ProcessId, _stateDir);
            if (_watchdog is null) { Log.Error("watchdog relaunch failed"); return; }
            _watchdog.EnableRaisingEvents = true;
            _watchdog.Exited += OnWatchdogExited;
            Log.Info($"watchdog relaunched (pid {_watchdog.Id})");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_quitMessage != 0 && (uint)msg == _quitMessage)
        {
            Log.Info("quit requested (--quit)");
            try { QuitRequested?.Invoke(); } catch (Exception e) { Log.Error("QuitRequested handler failed", e); }
            handled = true;
            return IntPtr.Zero;
        }
        if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated)
        {
            var hidden = false;
            // Under _gate and honouring _restored, like ReHideAfterShow: a Dispose on another thread (crash path) must
            // not be followed by a re-hide, because it also stops the watchdog. The event is raised outside the lock
            // (its handlers call into other components); none of them takes _gate, so there is no deadlock either way.
            lock (_gate)
            {
                if (!_started || _restored || _stopping) return IntPtr.Zero;
                try { TaskbarNative.HideAll(); hidden = true; } catch (Exception e) { Log.Error("re-hide failed", e); }
            }
            // Auto-hide is set off the UI thread (synchronous call into a just-restarted Explorer).
            EnsureAutoHideInBackground();
            try { TaskbarRecreated?.Invoke(); } catch (Exception e) { Log.Error("TaskbarRecreated handler failed", e); }
            Log.Info($"TaskbarCreated received -> re-hid taskbar={hidden}; appbar re-registered={AppBarReRegistered}");
        }
        return IntPtr.Zero;
    }

    private static void ApplyHidden()
    {
        TaskbarNative.SetAutoHide(true);
        TaskbarNative.HideAll();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_started || _restored) return;
            _stopping = true;

            // Unhook first: the Restore below shows the taskbar, which must not be hidden again.
            try { _showHook?.Dispose(); } catch (Exception e) { Log.Error("taskbar show hook dispose failed", e); }
            _showHook = null;
            try { _resumeTimer?.Stop(); } catch (Exception) { }

            try
            {
                if (_source is not null)
                {
                    _source.RemoveHook(WndProc);
                    _source.Dispose();
                    _source = null;
                }
            }
            catch (Exception e) { Log.Error("watcher window dispose failed", e); }

            try
            {
                // _autoHideGate: wait for an in-flight background auto-hide, so it cannot land after the restore.
                lock (_autoHideGate)
                {
                    if (!TaskbarNative.Restore(_original!))
                    {
                        // Explorer is mid-restart: keep the state file and let the watchdog finish the job.
                        Log.Info("taskbar restore: no explorer tray found; leaving state file and watchdog in place");
                        return;
                    }
                    _restored = true;
                }
                Log.Info("taskbar restored");
            }
            catch (Exception e)
            {
                Log.Error("taskbar restore failed", e);
                return;
            }

            try { _store.Delete(); } catch (Exception e) { Log.Error("state delete failed", e); }

            try
            {
                if (_watchdog is { HasExited: false }) _watchdog.Kill();
                _watchdog?.Dispose();
            }
            catch (Exception e) { Log.Error("watchdog stop failed", e); }
            _watchdog = null;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessage(string name);
}
