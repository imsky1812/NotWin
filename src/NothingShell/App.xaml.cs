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
    private Launcher.AppIndex? _index;
    private Notch.NotchController? _notch;
    private Timing.TimerService? _timer;
    private Clips.ClipboardService? _clips;
    private readonly Power.KeepAwakeService _awake = new();
    private Forecast.WeatherService? _weather;

    private static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NothingShell");
    private Media.MediaSessionService? _media;
    private Notifications.NotificationService? _notifications;
    private Panel.QuickPanelController? _panel;
    private bool _secondInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // `--quit`: ask the running shell to quit cleanly, then exit (never becomes a shell itself).
        if (QuitCommand.IsRequested(e.Args))
        {
            _secondInstance = true;
            Shutdown(QuitCommand.Run());
            return;
        }
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
        var hasIdentity = PackageIdentity.HasIdentity(out var packageName);
        Log.Info($"package identity={hasIdentity} name={packageName ?? "(none)"}");

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
        _taskbar.QuitRequested += () => Shutdown(); // `NothingShell.exe --quit`: same exit path as the menu's Quit

#if DEBUG
        Theme.FontCheck.Run(Resources);
#endif
        // Low-level hooks (mouse edge, Win key) run on their own message-loop thread so a busy UI thread can't
        // make Windows drop them.
        _hooks = new HookThread("NothingShell.Hooks");

        // App index (launcher search, pin names): first build in the background; nothing waits for it.
        _index = new Launcher.AppIndex(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NothingShell"));
        _ = _index.RefreshIfStaleAsync(TimeSpan.Zero);

        // 4. Dock, then 5. StartWithWindows (only from settings that were actually read).
        var (settings, loadedOk) = StartDock();
        ApplyStartWithWindows(settings, loadedOk);

        // 6. Lone Win key -> launcher. LL hooks live on their own thread so a busy UI thread can't lose them.
        StartLauncher();
    }

    private void StartLauncher()
    {
        var index = _index!;
        _launcher = new Launcher.LauncherController(index, new Launcher.FileSearchService());
        // Notch (spec §6.4): resting time/battery, "Opening ‹name›…" after a launch; hidden in full screen.
        try
        {
            var dockCtl = _controller;
            _notch = new Notch.NotchController(_tray!, _mouse,
                dockCtl is null ? null : () => dockCtl.Settings,
                dockCtl is null ? null : dockCtl.UpdateSettings);
            _notch.SetFullscreen(_fullscreen!.IsFullscreen);

            // The shared timer (spec §6.6): notch tile + live state now, the desktop widget in Plan 5.
            var minutes = dockCtl?.Settings.TimerMinutes ?? 25;
            _timer = new Timing.TimerService(minutes, m => dockCtl?.UpdateSettings(s => s with { TimerMinutes = m }));
            _notch.AttachTimer(_timer);
            _weather = new Forecast.WeatherService(DataDir);
            _notch.SettingsRequested += ShowSettings;
            if (dockCtl is not null) dockCtl.SettingsRequested += ShowSettings;
            _weather.Start(dockCtl?.Settings.Weather);
            try { _clips = new Clips.ClipboardService(DataDir); }
            catch (Exception e) { Log.Error("clipboard history failed to start", e); }
            var timer = _timer;
            var tools = new Tools.ToolRunner(_notch, () => _notch?.RequestSettings());
            _notch.TileFactory = kind => kind switch
            {
                Core.Notch.TileKind.Timer => new Notch.Dashboard.Tiles.TimerTile(timer),
                Core.Notch.TileKind.Media when _media is not null => new Notch.Dashboard.Tiles.MediaTile(_media, MediaAppName),
                Core.Notch.TileKind.Clipboard when _clips is not null => new Notch.Dashboard.Tiles.ClipboardTile(_clips, () => _notch!),
                Core.Notch.TileKind.Notes => new Notch.Dashboard.Tiles.NotesTile(new NotesStore(DataDir), () => _notch!),
                Core.Notch.TileKind.KeepAwake => new Notch.Dashboard.Tiles.KeepAwakeTile(_awake),
                Core.Notch.TileKind.Weather when _weather is not null => new Notch.Dashboard.Tiles.WeatherTile(_weather, () => _notch?.RequestSettings()),
                Core.Notch.TileKind.Shelf => new Notch.Dashboard.Tiles.ShelfTile(new Core.Shelving.ShelfStore(DataDir), new Dock.AppIconCache()),
                Core.Notch.TileKind.GrabText or Core.Notch.TileKind.Color or Core.Notch.TileKind.Snip
                    or Core.Notch.TileKind.Phone or Core.Notch.TileKind.Draw or Core.Notch.TileKind.Settings
                    => new Notch.Dashboard.Tiles.ToolTile(kind, tools.Run),
                _ => new Notch.Dashboard.PlaceholderTile(kind),
            };
            _fullscreen.Changed += fs => _notch?.SetFullscreen(fs);
            _launcher.Launched += name => _notch?.Opening(name);

            // Music state: the system media session (Changed arrives on this UI thread).
            _media = new Media.MediaSessionService();
            _media.Changed += snap => _notch?.SetMusic(snap.Playing, snap.Art);
            _ = _media.StartAsync();

            // Notifications: shown in the notch for 3.6 s (and listed in the panel, Task 12).
            _notifications = new Notifications.NotificationService();
            var notchIcons = new Dock.AppIconCache();
            _notifications.Added += item => _notch?.Notify(
                new Core.Notch.NotchNotification(item.AppId, item.AppName, item.Text),
                string.IsNullOrEmpty(item.AppId) ? null : notchIcons.Get(item.AppId, 40));
            _ = _notifications.StartAsync();
            var notifications = _notifications;
            _timer.Finished += () =>
            {
                Log.Info("timer finished");
                _notch?.NotifyLocal("Timer", "Time is up", "timer");
                notifications.AddLocal("Timer", "Time is up"); // kept in the panel, also when full screen hid the notch
            };
        }
        catch (Exception e) { Log.Error("notch failed to start", e); }
        if (_dock is not null)
        {
            _dock.StartClicked += _launcher.Toggle;
            _dock.SearchClicked += _launcher.Toggle;
        }
        _fullscreen!.Changed += fs => { if (fs) { _launcher?.Close(); _panel?.Close(); } };

        // Quick-settings panel (spec §6.3): Win+A / Win+N (below) and the dock tray. Guarded like the notch: a failure
        // here must never take the dock and launcher down with it (Win+A / Win+N then simply do nothing).
        try
        {
            var panel = new Panel.QuickPanelController(_reveal);
            var tiles = new Panel.TilesSection(panel.Window.TilesHost);
            panel.Opening += tiles.Refresh;
            var sliders = new Panel.SlidersSection(panel.Window.SlidersHost, _tray!, new Panel.BrightnessService());
            panel.Opening += sliders.Refresh;
            if (_notifications is not null && _controller is not null)
            {
                var controller = _controller;
                var notes = new Panel.NotificationsSection(panel.Window.NotificationsHost, _notifications,
                    () => controller.Settings, controller.UpdateSettings, panel.Close, () => panel.IsOpen);
                panel.Opening += () => _ = notes.RefreshAsync();
            }
            _panel = panel;
            if (_dock is not null) _dock.TrayClicked += panel.Toggle;
        }
        catch (Exception e) { Log.Error("quick-settings panel failed to start", e); }

        try
        {
            _winKeys = new Launcher.WinKeyListener(_hooks!, Dispatcher);
            _winKeys.LauncherRequested += _launcher.Toggle;
            _winKeys.PanelRequested += () => _panel?.Toggle();
        }
        catch (Exception e)
        {
            // Without the hook Win simply opens Windows' Start, which is a safe fallback.
            Log.Error("win key hook failed; Win opens Windows' Start", e);
        }
    }

    // Settings window (spec §6.7): the dock controller is the one settings writer.
    private void ShowSettings()
    {
        var dock = _controller;
        if (dock is null) { Log.Info("settings: no dock controller, nothing to edit"); return; }
        Settings.SettingsWindow.ShowOrActivate(() => dock.Settings, dock.SetStartWithWindows, dock.SetKeepVisible, _weather,
            place =>
            {
                dock.UpdateSettings(s => s with { Weather = place });
                _weather?.Start(place);
            },
            () => Shutdown());
    }

    // Source app of a media session (AUMID or exe) as a short name for the media tile: the launcher index, else the
    // exe name without ".exe", else "Media".
    private string MediaAppName(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "Media";
        if (_index?.FindApp(id) is { } app) return app.Name;
        var file = Path.GetFileName(id);
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(file) : "Media";
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

        _controller = new DockController(_dock, vm, _reveal, store, settings, _appBar!, Shutdown, id => _index?.FindApp(id)?.Name);
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
            _mouse = new MouseEdgeHook(_hooks!, startPaused: _fullscreen.IsFullscreen);
            _mouse.Moved += _reveal.OnCursorMoved;
        }

        _fullscreen.Changed += fullscreen =>
        {
            Log.Info($"fullscreen={fullscreen}");
            if (_mouse is not null) _mouse.Paused = fullscreen; // no hook cost while a game/video is in front
            if (fullscreen) _controller?.ClosePopups();          // a menu left open would sit over the game/video
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
            _media?.Dispose();
            _notifications?.Dispose();
            _panel?.Dispose();
            _notch?.Dispose();
            _timer?.Dispose();
            _clips?.Dispose();
            _awake.Dispose(); // keep awake never outlives the shell
            _weather?.Dispose();
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
