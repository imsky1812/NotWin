using System.IO;
using System.Windows;
using NothingShell.Core;
using NothingShell.Dock;
using NothingShell.Interop;
using NothingShell.Shell;

namespace NothingShell;

public partial class App : Application
{
    private TaskbarController? _taskbar;
    private DockWindow? _dock;
    private DockReveal? _reveal;
    private MouseEdgeHook? _mouse;
    private FullscreenMonitor? _fullscreen;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Register crash/exit restore paths before the taskbar is touched.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try { _taskbar?.Dispose(); }
            finally { Log.Error("unhandled exception", args.ExceptionObject as Exception); }
        };
        DispatcherUnhandledException += (_, args) =>
        {
            try { _taskbar?.Dispose(); }
            finally { Log.Error("dispatcher unhandled exception", args.Exception); }
            // Not marked handled: the crash continues.
        };
        SessionEnding += (_, _) => _taskbar?.Dispose();

        var uiAccess = TokenInfo.GetUiAccess() ? 1 : 0;
        Log.Info($"startup uiAccess={uiAccess} pid={Environment.ProcessId}");

        _taskbar = new TaskbarController();
        _taskbar.Start();

#if DEBUG
        Theme.FontCheck.Run(Resources);
#endif
        StartDock();
    }

    private void StartDock()
    {
        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NothingShell");
        var settings = new SettingsStore(settingsDir).Load();

        var vm = new DockViewModel();
        _dock = new DockWindow(vm);
        _dock.Initialize();
        _dock.Closed += (_, _) => Shutdown(); // WM_CLOSE to the (usually hidden) dock window quits the shell

        var dock = _dock;
        _reveal = new DockReveal(vm, () => dock.Placement);
        _dock.HoverChanged += _reveal.SetHoverDock;
        _dock.PlacementChanged += _reveal.Refresh;

        _fullscreen = new FullscreenMonitor();
        _reveal.SetFullscreen(_fullscreen.IsFullscreen);
        _reveal.SetKeepVisible(settings.KeepDockVisible);

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
            _reveal?.SetFullscreen(fullscreen);
        };

        Log.Info($"dock ready keepVisible={settings.KeepDockVisible} fullscreen={_fullscreen.IsFullscreen} selfTest={selfTest}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _mouse?.Dispose();
            _fullscreen?.Dispose();
            _reveal?.Dispose();
        }
        catch (Exception ex) { Log.Error("dock shutdown failed", ex); }
        _taskbar?.Dispose();
        Log.Info("exit");
        base.OnExit(e);
    }
}
