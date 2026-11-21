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
    private Widgets.SnakeWidget? _snake;
    private Launcher.LauncherController? _launcher;
    private Launcher.AppIndex? _index;
    private Notch.NotchController? _notch;
    private Timing.TimerService? _timer;
    private Clips.ClipboardService? _clips;
    private readonly Power.KeepAwakeService _awake = new();
    private Forecast.WeatherService? _weather;
    private GoogleCalendar.GoogleCalendarService? _calendar;
    private Notch.Dashboard.SharedNotes? _notes; // the notch tile and the desktop widget share one note (Plan 9)
    private Alarms.AlarmService? _alarms;
    private readonly System.Windows.Threading.DispatcherTimer _alarmTick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly System.Windows.Threading.DispatcherTimer _alarmQuiet = new() { Interval = TimeSpan.FromSeconds(60) };
    private System.Windows.Media.MediaPlayer? _alarmSound;
    private Alarms.AlarmRingWindow? _ringWindow;
    private Sync.SyncService? _sync;
    private string _syncStatus = "OFF";
    private System.IO.FileSystemWatcher? _syncWatch;
    private readonly System.Windows.Threading.DispatcherTimer _syncTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly System.Windows.Threading.DispatcherTimer _syncSoon = new() { Interval = TimeSpan.FromSeconds(2) };
    private Widgets.DesktopWidgets? _widgets;
    private readonly Core.Widgets.SnakeKeyGate _snakeGate = new(); // one gate for whichever snake is on the desktop
    private Dock.AppIconCache? _folderIcons; // shared by every app folder

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
            _widgets?.Rebuild(); // backup for the widget windows' own loss signal
        };
        _taskbar.TaskbarReHidden += () => _mouse?.Reinstall();
        _taskbar.QuitRequested += () => Shutdown(); // `NothingShell.exe --quit`: same exit path as the menu's Quit

#if DEBUG
        Theme.FontCheck.Run(Resources);
#endif
        // App launches go through Explorer (normal integrity); report any fallback.
        ShellLinkNative.OnError = (message, ex) => { if (ex is null) Log.Info(message); else Log.Error(message, ex); };

        // Low-level hooks (mouse edge, Win key) run on their own message-loop thread so a busy UI thread can't
        // make Windows drop them.
        _hooks = new HookThread("NothingShell.Hooks");

        // App index (launcher search, pin names): first build in the background; nothing waits for it.
        _index = new Launcher.AppIndex(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NothingShell"));
        _ = _index.RefreshIfStaleAsync(TimeSpan.Zero);

        // 4. Dock, then 5. StartWithWindows (only from settings that were actually read).
        SyncAtStart(); // Plan 9: a newer copy from another PC is in place before anything reads the settings
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
            // Alarms (Plan 9): checked every second; guarded like the calendar.
            try { StartAlarms(); }
            catch (Exception e) { Log.Error("alarms failed to start", e); }
            // Google Calendar (Plan 6): guarded, a failure leaves the calendar widget showing plain dates.
            try
            {
                _calendar = new GoogleCalendar.GoogleCalendarService(DataDir);
                _calendar.Start();
            }
            catch (Exception e) { Log.Error("google calendar failed to start", e); }
            try { _clips = new Clips.ClipboardService(DataDir); }
            catch (Exception e) { Log.Error("clipboard history failed to start", e); }
            var timer = _timer;
            var tools = new Tools.ToolRunner(_notch, () => _notch?.RequestSettings());
            _notch.TileFactory = kind => kind switch
            {
                Core.Notch.TileKind.Timer => new Notch.Dashboard.Tiles.TimerTile(timer),
                Core.Notch.TileKind.Media when _media is not null => new Notch.Dashboard.Tiles.MediaTile(_media, MediaAppName),
                Core.Notch.TileKind.Clipboard when _clips is not null => new Notch.Dashboard.Tiles.ClipboardTile(_clips, () => _notch!),
                Core.Notch.TileKind.Notes => new Notch.Dashboard.Tiles.NotesTile(_notes ??= new Notch.Dashboard.SharedNotes(new NotesStore(DataDir)), () => _notch!),
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
            _media.Changed += snap => _notch?.SetMusic(snap.Playing, snap.Art, snap.Title, snap.Artist);
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
        _launcher.VisibleChanged += _ => UpdateNotchCover();

        // Desktop widgets (spec §6.5): on the desktop layer, sharing the timer / media / weather services. Guarded: a
        // failure here must never take the dock, launcher or notch down.
        try { StartWidgets(); }
        catch (Exception e) { Log.Error("desktop widgets failed to start", e); }

        try { StartSync(); }
        catch (Exception e) { Log.Error("sync failed to start", e); }

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
            panel.Window.IsVisibleChanged += (_, _) => UpdateNotchCover(); // after it is really shown / hidden
            if (_dock is not null) _dock.TrayClicked += panel.Toggle;
        }
        catch (Exception e) { Log.Error("quick-settings panel failed to start", e); }

        try
        {
            _winKeys = new Launcher.WinKeyListener(_hooks!, Dispatcher);
            _winKeys.LauncherRequested += _launcher.Toggle;
            _winKeys.PanelRequested += () => _panel?.Toggle();
            _winKeys.EscapePressed += () => _widgets?.EndEdit();
            _winKeys.SnakeGate = _snakeGate;
            _winKeys.SnakeKeyPressed += key => _snake?.OnKey(key);
            _winKeys.SnakeReleased += () => _snake?.Release();
        }
        catch (Exception e)
        {
            // Without the hook Win simply opens Windows' Start, which is a safe fallback.
            Log.Error("win key hook failed; Win opens Windows' Start", e);
        }
    }

    // The open dashboard must never sit on top of the launcher or the panel (user feedback 2026-11-19).
    private void UpdateNotchCover() =>
        _notch?.SetCovered(_launcher?.IsOpen == true || _panel?.IsOpen == true);

    private void StartWidgets()
    {
        var dock = _controller;
        if (dock is null || _timer is null) return;
        var widgets = new Widgets.DesktopWidgets(() => dock.Settings, dock.UpdateSettings, (p, save) => CreateWidget(p, save, dock));
        _widgets = widgets;
        _widgets.EditingChanged += editing =>
        {
            if (_winKeys is not null) _winKeys.EscapeWanted = editing; // Esc ends edit mode
            if (editing) _snake?.Release();                            // the snake never keeps the keys while editing
        };
        if (_fullscreen is not null)
        {
            _fullscreen.Changed += fs => _widgets?.SetFullscreen(fs);
            if (_fullscreen.IsFullscreen) widgets.SetFullscreen(true);
        }
        widgets.Start();
    }

    // One widget object per desktop instance (Plan 5b); a removed instance's object is disposed by DesktopWidgets.
    private Widgets.IDesktopWidget? CreateWidget(Core.Widgets.WidgetPlacement p, Action<Core.Widgets.WidgetConfig> save, Dock.DockController dock)
    {
        switch (p.Kind)
        {
            case Core.Widgets.WidgetKind.Clock: return new Widgets.ClockWidget();
            case Core.Widgets.WidgetKind.Timer: return _timer is null ? null : new Widgets.TimerWidget(_timer);
            case Core.Widgets.WidgetKind.Calendar: return new Widgets.CalendarWidget(_calendar, () => _widgets?.Editing == true);
            case Core.Widgets.WidgetKind.Agenda: return new Widgets.AgendaWidget(_calendar);
            case Core.Widgets.WidgetKind.Notes: return new Widgets.NotesWidget(_notes ??= new Notch.Dashboard.SharedNotes(new NotesStore(DataDir)));
            case Core.Widgets.WidgetKind.Alarm: return _alarms is null ? null : new Alarms.AlarmWidget(_alarms);
            case Core.Widgets.WidgetKind.TicTacToe:
                return new Widgets.TicTacToeWidget(() => dock.Settings.TicTacToeScore, score => dock.UpdateSettings(s => s with { TicTacToeScore = score }));
            case Core.Widgets.WidgetKind.Snake:
                return _snake = new Widgets.SnakeWidget(_snakeGate, dock.Settings.SnakeBest,
                    best => dock.UpdateSettings(s => s with { SnakeBest = Math.Max(s.SnakeBest, best) }));
            case Core.Widgets.WidgetKind.Weather: return _weather is null ? null : new Widgets.WeatherWidget(_weather);
            case Core.Widgets.WidgetKind.Media: return _media is null ? null : new Widgets.MediaWidget(_media);
            case Core.Widgets.WidgetKind.Battery:
                // The Bluetooth watcher runs only while a battery widget is on the desktop (the widget owns it).
                return _tray is null ? null : new Widgets.BatteryWidget(new Widgets.DeviceBatteryService(_tray), ownsSource: true);
            case Core.Widgets.WidgetKind.Photo:
                return new Widgets.PhotoFrameWidget(p.Id, p.Config, new Widgets.PhotoStore(Path.Combine(DataDir, "frames")), save);
            case Core.Widgets.WidgetKind.Folder:
                if (_index is not { } index) return null;
                _folderIcons ??= new Dock.AppIconCache();
                return new Widgets.FolderWidget(p.Id, p.Config, () => index.Apps, index.FindApp, _folderIcons, save);
            default: return null;
        }
    }

    // ---- Sync (Plan 9) ----

    private static void SyncAtStart()
    {
        try
        {
            var folder = new SettingsStore(DataDir).Load().SyncFolder;
            if (folder is null) return;
            var outcome = new Sync.SyncService(DataDir, () => folder).RunOnce();
            Log.Info($"sync at start: reached={outcome.Reached}");
        }
        catch (Exception e) { Log.Error("sync at start failed", e); }
    }

    private void StartSync()
    {
        _sync = new Sync.SyncService(DataDir, () => _controller?.Settings.SyncFolder);
        _syncTimer.Tick += (_, _) => SyncNow();
        _syncSoon.Tick += (_, _) => { _syncSoon.Stop(); SyncNow(); };
        _syncTimer.Start();
        WatchSyncFolder();
        SyncNow();
    }

    // The folder's copy changed (another PC, through OneDrive): sync two seconds after the last change.
    private void WatchSyncFolder()
    {
        _syncWatch?.Dispose();
        _syncWatch = null;
        if (_controller?.Settings.SyncFolder is null || _sync is null) return;
        try
        {
            if (_sync.WatchDirectory() is not { } dir) return; // the folder is gone: never recreated; the 30 s check reports it
            var watch = new System.IO.FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            void Soon(object? s, FileSystemEventArgs e) => Dispatcher.BeginInvoke(() => { _syncSoon.Stop(); _syncSoon.Start(); });
            watch.Changed += Soon;
            watch.Created += Soon;
            watch.Renamed += (s, e) => Soon(s, e);
            watch.EnableRaisingEvents = true;
            _syncWatch = watch;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Info("sync: the folder cannot be watched (" + e.GetType().Name + "), checking every 30 s");
        }
    }

    private void SyncNow()
    {
        if (_sync is null || _controller is not { } dock) return;
        if (dock.Settings.SyncFolder is null) { _syncStatus = "OFF"; return; }
        Sync.SyncOutcome outcome;
        try { outcome = _sync.RunOnce(); }
        catch (Exception e) { Log.Error("sync failed", e); _syncStatus = "SYNC FAILED"; return; }
        _syncStatus = outcome.Reached ? $"SYNCED {DateTime.Now:HH:mm}" : "CAN'T REACH THE FOLDER";
        if (outcome.Pulled.Contains("settings.json"))
        {
            var before = dock.Settings;
            var pulled = new SettingsStore(DataDir).Load();
            dock.ApplyExternal(pulled);
            if (_widgets is { } widgets)
            {
                if (pulled.WidgetScale != before.WidgetScale) widgets.SetScale(pulled.WidgetScale);
                if (pulled.WidgetList is { } layout) widgets.Apply(layout);
            }
            if (!Equals(pulled.Weather, before.Weather)) _weather?.Start(pulled.Weather);
        }
        if (outcome.Pulled.Contains("notes.txt")) _notes?.Reload();
        if (outcome.Pulled.Contains(Core.Alarms.AlarmStore.FileName)) _alarms?.Reload();
    }

    private void SetSyncFolder(string? folder)
    {
        _controller?.UpdateSettings(s => s with { SyncFolder = folder });
        WatchSyncFolder();
        SyncNow();
    }

    // Alarms (Plan 9): a ring loops Windows' alarm sound for up to a minute, shows in the notch and the panel's list, and
    // puts the Snooze / Stop card at the top of the screen (after full screen ends, if it is still ringing then).
    private void StartAlarms()
    {
        var alarms = new Alarms.AlarmService(new Core.Alarms.AlarmStore(DataDir), () => DateTime.Now);
        _alarms = alarms;
        _alarmTick.Tick += (_, _) =>
        {
            try { alarms.Check(); }
            catch (Exception e) { Log.Error("alarm check failed", e); }
        };
        _alarmTick.Start();
        _alarmQuiet.Tick += (_, _) => { _alarmQuiet.Stop(); alarms.Stop(); };
        alarms.Ringing += alarm =>
        {
            var time = $"{alarm.Hour:00}:{alarm.Minute:00}";
            try
            {
                _alarmSound ??= new System.Windows.Media.MediaPlayer();
                var player = _alarmSound;
                player.Open(new Uri(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav")));
                player.MediaEnded -= LoopAlarm;
                player.MediaEnded += LoopAlarm;
                player.Play();
            }
            catch (Exception e) { Log.Error("alarm sound failed", e); }
            _alarmQuiet.Stop();
            _alarmQuiet.Start();
            _notch?.NotifyLocal("Alarm", time, "timer");
            _notifications?.AddLocal("Alarm", time);
            if (_fullscreen?.IsFullscreen != true) ShowRing(alarm);
        };
        alarms.Quiet += () =>
        {
            _alarmQuiet.Stop();
            try { _alarmSound?.Stop(); } catch (Exception e) { Log.Error("alarm sound stop failed", e); }
            var ring = _ringWindow;
            _ringWindow = null;
            try { ring?.Close(); } catch (InvalidOperationException) { }
        };
        if (_fullscreen is not null)
            _fullscreen.Changed += fs => { if (!fs && alarms.RingingNow is { } a && _ringWindow is null) ShowRing(a); };
    }

    private void LoopAlarm(object? sender, EventArgs e)
    {
        if (_alarms?.RingingNow is null || _alarmSound is not { } p) return;
        p.Position = TimeSpan.Zero;
        p.Play();
    }

    private void ShowRing(Core.Alarms.Alarm alarm)
    {
        if (_alarms is not { } alarms) return;
        try
        {
            _ringWindow?.Close();
            _ringWindow = new Alarms.AlarmRingWindow(alarm, alarms.Snooze, alarms.Stop);
            _ringWindow.Show();
        }
        catch (Exception e) { Log.Error("alarm card failed", e); }
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
            () => Shutdown(),
            new Settings.SettingsWindow.WidgetOptions(
                scale => { if (_widgets is null) dock.UpdateSettings(s => s with { WidgetScale = scale }); else _widgets.SetScale(scale); },
                Path.Combine(AppContext.BaseDirectory, "Wallpapers"),
                () => _widgets?.BeginEdit(),
                Path.Combine(DataDir, "wallpapers")),
            new Settings.SettingsWindow.DockEffects(
                on => { dock.UpdateSettings(s => s with { WindowPreviews = on }); dock.PreviewsEnabled = on; }),
            _calendar,
            new Settings.SettingsWindow.SyncOptions(() => dock.Settings.SyncFolder, () => _syncStatus, SetSyncFolder));
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
        // Plan 7: dock window previews (switchable in Settings). Minimizing uses Windows' own animation (user, 2026-11-21).
        _controller.PreviewsEnabled = _controller.Settings.WindowPreviews;
        _controller.Start();

        _fullscreen = new FullscreenMonitor();
        _reveal.SetFullscreen(_fullscreen.IsFullscreen);
        // A maximized app in front lets a kept-visible dock slide down (user, 2026-11-21).
        _reveal.SetMaximizedFront(_fullscreen.IsMaximizedFront);
        _fullscreen.MaximizedChanged += maximized => _reveal?.SetMaximizedFront(maximized);

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
            _calendar?.Dispose();
            _alarmTick.Stop();
            _syncTimer.Stop();
            _syncWatch?.Dispose();
            try { _alarmSound?.Stop(); } catch { /* exiting */ }
            _widgets?.Dispose();
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
