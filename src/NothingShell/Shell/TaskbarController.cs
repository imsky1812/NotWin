using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
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

    public TaskbarController(string? stateDir = null)
    {
        _stateDir = stateDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NothingShell");
        _store = new TaskbarRestoreStore(_stateDir);
    }

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
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreated != 0 && (uint)msg == _taskbarCreated)
        {
            Log.Info("TaskbarCreated: re-hiding taskbar");
            try { ApplyHidden(); } catch (Exception e) { Log.Error("re-hide failed", e); }
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
                if (!TaskbarNative.Restore(_original!))
                {
                    // Explorer is mid-restart: keep the state file and let the watchdog finish the job.
                    Log.Info("taskbar restore: no explorer tray found; leaving state file and watchdog in place");
                    return;
                }
                _restored = true;
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
