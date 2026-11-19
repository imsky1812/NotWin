using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NothingShell.Core;
using NothingShell.Core.Notch;
using NothingShell.Dock;
using NothingShell.Notch.Dashboard;
using NothingShell.Shell;

namespace NothingShell.Notch;

/// <summary>
/// Drives the notch: feeds the state machine (launcher opening, media, notifications, full screen) and shows what it
/// says. No polling: one timer is armed only for the end of a transient, and the clock follows the tray's minute tick.
/// Plan 4: the global mouse feed drives <see cref="NotchHover"/>, which opens the dashboard; a second timer is armed
/// only for the hover's next deadline (open dwell / close delay).
/// </summary>
public sealed class NotchController : IDisposable, IDashboardHost
{
    private readonly NotchStateMachine _machine = new();
    private readonly NotchWindow _window = new();
    private readonly TrayStatusService _tray;
    private readonly DispatcherTimer _transient;
    private NotchState _last = (NotchState)(-1);
    private bool? _plugged; // null until the first reading: the start-up value is not a plug event
    private readonly NotchHover _hover = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _hoverTimer;
    private readonly DashboardView _dash;
    private readonly MouseEdgeHook? _mouse;
    private readonly Func<ShellSettings>? _getSettings;
    private readonly Action<Func<ShellSettings, ShellSettings>>? _updateSettings;
    private DashboardLayout? _localLayout; // used only when there is no settings owner (dock failed to start)
    private int _page;
    private bool _editing;

    /// <param name="mouse">The global mouse feed (null: the dashboard never opens).</param>
    /// <param name="getSettings">Current settings (layout), from the one settings writer (DockController).</param>
    /// <param name="updateSettings">Applies and saves a settings change.</param>
    public NotchController(TrayStatusService tray, MouseEdgeHook? mouse = null, Func<ShellSettings>? getSettings = null,
        Action<Func<ShellSettings, ShellSettings>>? updateSettings = null)
    {
        _tray = tray;
        _mouse = mouse;
        _getSettings = getSettings;
        _updateSettings = updateSettings;
        _transient = new DispatcherTimer();
        _transient.Tick += (_, _) => { _transient.Stop(); Render(); };
        _hoverTimer = new DispatcherTimer(DispatcherPriority.Input);
        _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); PumpHover(); };
        _dash = new DashboardView(k => TileFactory?.Invoke(k) ?? new PlaceholderTile(k));
        _dash.PageRequested += p => { _page = p; ShowDashboard(); };
        _dash.EditToggled += ToggleEdit;
        _dash.SizeChipClicked += i => UpdateLayout(l => l.CycleSize(_page, i));
        _dash.RemoveClicked += i => UpdateLayout(l => l.Remove(_page, i));
        _dash.AddClicked += () => UpdateLayout(l => l.AddBack(_page));
        _dash.OverlayChanged += () => _window.SetOpenContent(_dash.Root, _dash.Height);
        _window.SettingsRequested += () => SettingsRequested?.Invoke();
        _window.EscPressed += Close;
        _window.TypingEnded += () => Hold("text", false);
        if (_mouse is not null) _mouse.Moved += OnMoved;
        _tray.PropertyChanged += OnTrayChanged;
        _tray.DefaultDeviceChanged += _window.RebindMeter;
        _window.Initialize();
        UpdateClockAndBattery();
        _plugged = _tray.HasBattery ? _tray.PluggedIn : null;
        Render();
    }

    public void Opening(string name)
    {
        _machine.Opening(name, DateTimeOffset.Now);
        if (_machine.OpeningName == name) _window.SetOpening(name);
        Render();
    }

    public void Notify(NotchNotification n, ImageSource? icon)
    {
        _machine.Notify(n, DateTimeOffset.Now);
        if (_machine.CurrentNotification == n) _window.SetNotification(n, icon);
        Render();
    }

    /// <summary>Nothing Shell's own card in the notch (timer, tools): same rules as a notification (3.6 s, hidden in full screen).</summary>
    public void NotifyLocal(string title, string text, string icon)
    {
        var n = new NotchNotification("", title, text);
        _machine.Notify(n, DateTimeOffset.Now);
        if (_machine.CurrentNotification == n) _window.SetNotification(n, TileChrome.IconImage(icon));
        Render();
    }

    /// <summary>Follows the shared timer: the live Timer state while it runs (spec §6.4 priority).</summary>
    public void AttachTimer(Timing.TimerService timer)
    {
        _timer = timer;
        timer.Changed += OnTimerChanged;
        OnTimerChanged();
    }

    private Timing.TimerService? _timer;

    private void OnTimerChanged()
    {
        if (_timer is null) return;
        _window.SetTimer(_timer.Fraction, _timer.Text, _timer.Running, _timer.FlashOn);
        _machine.SetTimerRunning(_timer.Running);
        Render();
    }

    public void SetMusic(bool playing, ImageSource? art)
    {
        _machine.SetMusicPlaying(playing);
        _window.SetMusicArt(art);
        Render();
    }

    public void SetFullscreen(bool fullscreen)
    {
        var wasOpen = _hover.IsOpen;
        _hover.SetFullscreen(fullscreen);
        _machine.SetFullscreen(fullscreen);
        if (wasOpen && !_hover.IsOpen) ApplyOpen();
        else Render();
    }

    // ---- dashboard ----

    /// <summary>Creates the view for a tile kind (set by the App; kinds without one show a placeholder).</summary>
    public Func<TileKind, ITileView>? TileFactory { get; set; }

    public DashboardView Dashboard => _dash;

    public bool IsOpen => _hover.IsOpen;

    /// <summary>Right-click on the open notch, or a tile asking for Settings (spec §6.7).</summary>
    public event Action? SettingsRequested;

    public void RequestSettings() => SettingsRequested?.Invoke();

    /// <summary>The dashboard opened (true) or closed (false).</summary>
    public event Action<bool>? OpenChanged;

    /// <summary>The current layout, sanitized (a bad saved layout never breaks the dashboard).</summary>
    public DashboardLayout Layout => DashboardLayout.Sanitize(_getSettings is null ? _localLayout : _getSettings().NotchLayout);

    public void UpdateLayout(Func<DashboardLayout, DashboardLayout> change)
    {
        var next = change(Layout);
        if (_updateSettings is null) _localLayout = next;
        else _updateSettings(s => s with { NotchLayout = next });
        ShowDashboard();
    }

    public bool Editing => _editing;

    /// <summary>Keeps the dashboard open while held ("edit", "text", "expand").</summary>
    public void Hold(string reason, bool on)
    {
        _hover.Hold(reason, on, _clock.Elapsed);
        PumpHover();
    }

    /// <summary>A text box wants the keyboard (Notes): makes the notch activatable and holds it open.</summary>
    public void BeginTyping()
    {
        Hold("text", true);
        _window.ActivateForTyping();
    }

    public void EndTyping()
    {
        _window.EndTyping();
        Hold("text", false);
    }

    public void ShowOverlay(FrameworkElement content, int rows = 2)
    {
        Hold("expand", true);
        _dash.ShowOverlay(content, rows);
    }

    public void CloseOverlay()
    {
        _dash.CloseOverlay();
        Hold("expand", false);
    }

    public void ShowPageOf(TileKind kind)
    {
        var page = _dash.PageOf(kind);
        if (page < 0 || page == _page || !_hover.IsOpen) return;
        _page = page;
        ShowDashboard();
    }

    /// <summary>Closes the dashboard now (Esc, a tool starting); it reopens only after the pointer leaves and returns.</summary>
    public void Close()
    {
        if (!_hover.IsOpen) return;
        _hover.Close();
        ApplyOpen();
        PumpHover();
    }

    private void OnMoved(PointI p)
    {
        if (_last == NotchState.Hidden) return;
        _hover.Pointer(_window.OverPill(p), _window.OverOpenArea(p), _clock.Elapsed);
        PumpHover();
    }

    private void PumpHover()
    {
        if (_hover.Update(_clock.Elapsed)) ApplyOpen();
        _hoverTimer.Stop();
        if (_hover.NextCheckAt is { } at)
        {
            var wait = at - _clock.Elapsed;
            _hoverTimer.Interval = (wait > TimeSpan.Zero ? wait : TimeSpan.Zero) + TimeSpan.FromMilliseconds(5);
            _hoverTimer.Start();
        }
    }

    private void ApplyOpen()
    {
        var open = _hover.IsOpen;
        if (open) ShowDashboard();
        else
        {
            _editing = false;
            _window.EndTyping();
        }
        _machine.SetOpen(open);
        _dash.SetOpen(open);
        Render();
        OpenChanged?.Invoke(open);
    }

    private void ToggleEdit()
    {
        _editing = !_editing;
        _hover.Hold("edit", _editing, _clock.Elapsed);
        ShowDashboard();
        PumpHover();
    }

    private void ShowDashboard()
    {
        var layout = Layout;
        _page = Math.Clamp(_page, 0, Math.Max(0, layout.Pages.Count - 1));
        _dash.Show(layout, _page, _editing);
        _window.SetOpenContent(_dash.Root, _dash.Height);
    }

    private void OnTrayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrayStatusService.Time) or nameof(TrayStatusService.BatteryPercent)
            or nameof(TrayStatusService.Charging) or nameof(TrayStatusService.HasBattery)
            or nameof(TrayStatusService.PluggedIn))
        {
            UpdateClockAndBattery();
            CheckPlug();
        }
    }

    // Plug in / unplug: show the charging card once per change (desktops without a battery never show it).
    // Follows the charger (PowerSupplyStatus), not BatteryStatus: plugged in at 100 % or at a charge limit is "Idle".
    private void CheckPlug()
    {
        if (!_tray.HasBattery) { _plugged = null; return; }
        var plugged = _tray.PluggedIn;
        if (_plugged is { } was && was != plugged)
        {
            var now = DateTimeOffset.Now;
            _machine.PowerChanged(plugged, _tray.BatteryPercent, now);
            if (_machine.StateAt(now) == NotchState.Power && _machine.CurrentPower is { } p) _window.SetPower(p.Charging, p.Percent);
            Log.Info($"notch power plugged={plugged}");
            Render();
        }
        _plugged = plugged;
    }

    private void UpdateClockAndBattery()
    {
        _window.SetClock(DateTime.Now.ToString("HH:mm"));
        _window.SetBattery(_tray.HasBattery, _tray.BatteryPercent, _tray.Charging);
    }

    private void Render()
    {
        var now = DateTimeOffset.Now;
        var state = _machine.StateAt(now);
        _window.ShowState(state);
        if (state != _last) { Log.Info($"notch state={state}"); _last = state; }

        _transient.Stop();
        if (_machine.NextChangeAt(now) is { } at)
        {
            _transient.Interval = at - now + TimeSpan.FromMilliseconds(15);
            _transient.Start();
        }
    }

    public void Dispose()
    {
        _transient.Stop();
        _hoverTimer.Stop();
        if (_mouse is not null) _mouse.Moved -= OnMoved;
        if (_timer is not null) _timer.Changed -= OnTimerChanged;
        _dash.Dispose();
        _tray.PropertyChanged -= OnTrayChanged;
        _tray.DefaultDeviceChanged -= _window.RebindMeter;
        try { _window.Close(); } catch { }
    }
}
