using System.IO;
using System.Windows;
using NothingShell.Core;
using NothingShell.Dock;
using NothingShell.Interop;
using NothingShell.Native;
using NothingShell.Shell;
using NothingShell.Startup;

namespace NothingShell;

public partial class App : Application
{
    private TaskbarController? _taskbar;
    private DockWindow? _dock;
    private DockReveal? _reveal;
    private MouseEdgeHook? _mouse;
    private FullscreenMonitor? _fullscreen;
    private DockController? _controller;
    private TrayStatusService? _tray;
    private DockAppBar? _appBar;
    private HookThread? _hooks;
    private Launcher.WinKeyListener? _winKeys;
    private Launcher.LauncherController? _launcher;
    private bool _secondInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 1. Single instance: a second copy exits silently, before any UI.
        if (!SingleInstance.TryAcquire())
        {
            _secondInstance = true;
            Shutdown(0);
            return;
        }
        // Diagnoses slow teardown: compare with the "exit" line in the log.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.Info("process exit");

        // Software rendering: the shell's surfaces are small, flat and mostly idle. Measured on the dev PC (Release,
        // ~60 s idle): working set 96 -> 68 MB, private 50 -> 25 MB, same idle CPU (0) and the same reveal frame
        // pacing (~19 ms average frame gap) as hardware rendering. Must be set before any window is created.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Register crash/exit restore paths before the taskbar is touched. The appbar goes first: a leaked
        // reservation leaves a dead strip at the bottom of the screen until Explorer restarts.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try { RestoreShell(); }
            finally { Log.Error("unhandled exception", args.ExceptionObject as Exception); }
        };
        DispatcherUnhandledException += (_, args) =>
        {
            try { RestoreShell(); }
            finally { Log.Error("dispatcher unhandled exception", args.Exception); }
            // Not marked handled: the crash continues.
        };
        SessionEnding += (_, _) => RestoreShell();

        var uiAccess = TokenInfo.GetUiAccess() ? 1 : 0;
        Log.Info($"startup uiAccess={uiAccess} pid={Environment.ProcessId}");

        // 2. Conflict gate: nothing touches the taskbar while Fenn is running.
        if (!ConflictGate.CheckOrWait())
        {
            Log.Info("conflict gate: user quit");
            Shutdown(0);
            return;
        }

        // 3. Taskbar.
        _taskbar = new TaskbarController();
        _taskbar.Start();
        _appBar = new DockAppBar();
        // Explorer restarted: the taskbar was just hidden again; now re-claim the reserved strip.
        // Both events follow Explorer being busy, which is when Windows may have dropped the LL mouse hook: renew it.
        _taskbar.TaskbarRecreated += () =>
        {
            _taskbar.AppBarReRegistered = _appBar.ReRegister();
            _mouse?.Reinstall();
        };
        _taskbar.TaskbarReHidden += () => _mouse?.Reinstall();

#if DEBUG
        Theme.FontCheck.Run(Resources);
#endif
        // 4. Dock, then 5. StartWithWindows (only from settings that were actually read).
        var (settings, loadedOk) = StartDock();
        ApplyStartWithWindows(settings, loadedOk);

        // 6. Lone Win key -> launcher. LL hooks live on their own thread so a busy UI thread can't lose them.
        StartLauncher();
    }

    private void StartLauncher()
    {
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NothingShell");
        var index = new Launcher.AppIndex(dataDir);
        _ = index.RefreshIfStaleAsync(TimeSpan.Zero); // first index in the background; the launcher never waits for it
        _launcher = new Launcher.LauncherController(index, new Launcher.FileSearchService());
        _launcher.Launched += name => Log.Info($"launched \"{name}\""); // Plan 3: the notch shows "Opening ‹name›…"
        if (_dock is not null)
        {
            _dock.StartClicked += _launcher.Toggle;
            _dock.SearchClicked += _launcher.Toggle;
        }
        _fullscreen!.Changed += fs => { if (fs) _launcher?.Close(); };

        try
        {
            _hooks = new HookThread("NothingShell.Hooks");
            _winKeys = new Launcher.WinKeyListener(_hooks, Dispatcher);
            _winKeys.LauncherRequested += _launcher.Toggle;
        }
        catch (Exception e)
        {
            // Without the hook Win simply opens Windows' Start, which is a safe fallback.
            Log.Error("win key hook failed; Win opens Windows' Start", e);
        }
    }

    private static void ApplyStartWithWindows(ShellSettings settings, bool loadedOk)
    {
        if (!loadedOk) { Log.Info("start-with-windows: settings load failed, leaving the Run entry alone"); return; }
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        try
        {
            StartupRegistration.SetEnabled(settings.StartWithWindows, exe);
            Log.Info($"start-with-windows={settings.StartWithWindows}");
        }
        catch (Exception ex) { Log.Error("applying start-with-windows failed", ex); }
    }

    private void RestoreShell()
    {
        try { _appBar?.Dispose(); }
        finally { _taskbar?.Dispose(); }
    }

    private (ShellSettings Settings, bool LoadedOk) StartDock()
    {
        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NothingShell");
        var store = new SettingsStore(settingsDir);
        var loaded = store.Load();
        // A corrupt file is renamed to .bad and counts as a successful load, so defaults (StartWithWindows=true) apply.
        var loadedOk = store.LastLoadSucceeded;
        var settings = PinnedAppsImporter.ApplyFirstRun(loaded, store);

        var vm = new DockViewModel();
        _tray = vm.Tray;
        _tray.Start();
        _dock = new DockWindow(vm);
        _dock.Initialize();
        _dock.Closing += (_, _) => _appBar?.Dispose(); // every close path (WM_CLOSE, Shutdown) gives the strip back first
        _dock.Closed += (_, _) => Shutdown(); // WM_CLOSE to the (usually hidden) dock window quits the shell

        var dock = _dock;
        _reveal = new DockReveal(vm, () => dock.Placement);
        _dock.HoverChanged += _reveal.SetHoverDock;
        _dock.PlacementChanged += _reveal.Refresh;

        _controller = new DockController(_dock, vm, _reveal, store, settings, _appBar!, Shutdown);
        _controller.Start();

        _fullscreen = new FullscreenMonitor();
        _reveal.SetFullscreen(_fullscreen.IsFullscreen);

        var selfTest = false;
#if DEBUG
        var mode = DockSelfTest.Mode;
        selfTest = mode is not null;
        if (selfTest) DockSelfTest.Run(_dock, _reveal, vm, hold: mode == "hold");
#endif
        if (!selfTest)
        {
            _mouse = new MouseEdgeHook(startPaused: _fullscreen.IsFullscreen);
            _mouse.Moved += _reveal.OnCursorMoved;
        }

        _fullscreen.Changed += fullscreen =>
        {
            Log.Info($"fullscreen={fullscreen}");
            if (_mouse is not null) _mouse.Paused = fullscreen; // no hook cost while a game/video is in front
            _reveal?.SetFullscreen(fullscreen, fullscreen ? null : DockWindowNative.GetCursorPos());
        };

        Log.Info($"dock ready keepVisible={settings.KeepDockVisible} fullscreen={_fullscreen.IsFullscreen} selfTest={selfTest}");
        return (settings, loadedOk);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_secondInstance) { base.OnExit(e); return; }
        try
        {
            _appBar?.Dispose();
            _winKeys?.Dispose();
            _launcher?.Dispose();
            _controller?.Dispose();
            _tray?.Dispose();
            _mouse?.Dispose();
            _fullscreen?.Dispose();
            _reveal?.Dispose();
            _hooks?.Dispose();
        }
        catch (Exception ex) { Log.Error("dock shutdown failed", ex); }
        _taskbar?.Dispose();
        Log.Info("exit");
        SingleInstance.Release();
        base.OnExit(e);
    }
}
