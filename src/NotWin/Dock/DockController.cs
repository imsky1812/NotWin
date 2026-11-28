using System.Diagnostics;
using System.Windows;
using NotWin.Core;
using NotWin.Core.Dock;
using NotWin.Native;

namespace NotWin.Dock;

/// <summary>
/// Glue between the window tracker, the pinned list and the dock: builds the items, keeps the view-model
/// collection in sync (in place, so icons and hover survive), and runs click / right-click actions.
/// UI thread only.
/// </summary>
public sealed class DockController : IDisposable
{
    private const int IconPx = 44; // 22 DIP at 200%; WPF downsamples for lower DPI

    private readonly DockWindow _dock;
    private readonly DockViewModel _vm;
    private readonly DockReveal _reveal;
    private readonly WindowTracker _tracker;
    private readonly SettingsStore _store;
    private readonly AppIconCache _icons = new();
    private readonly bool _trace = Environment.GetEnvironmentVariable("NOTWIN_DOCK_TRACE") == "1";
    private ShellSettings _settings;
    private readonly DockAppBar _appBar;
    private readonly Action _quit;
    private readonly Func<string, string?>? _indexedName;
    private DockPopup? _popup;
    private string _lastLog = "";

    /// <param name="indexedName">The launcher index's display name for an app id (null when unknown), used to name new pins.</param>
    public DockController(DockWindow dock, DockViewModel vm, DockReveal reveal, SettingsStore store, ShellSettings settings,
        DockAppBar appBar, Action quit, Func<string, string?>? indexedName = null)
    {
        _dock = dock; _vm = vm; _reveal = reveal; _store = store; _settings = settings;
        _appBar = appBar; _quit = quit; _indexedName = indexedName;
        _tracker = new WindowTracker();
        _tracker.Changed += Rebuild;
        _tracker.Changed += RefreshPreview; // after Rebuild: windows opened / closed while the card is open
        _dock.ItemClicked += OnItemClicked;
        _dock.ItemHover += OnItemHover;
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); PumpPreview(); };
        _preview.Activate += hwnd => { ClosePreview(); _ = WindowListNative.Activate(hwnd); };
        _preview.CloseWindow += WindowListNative.RequestClose;
        _preview.PointerOver += over => { _previewHover.Pointer(_hoverItem, over, _previewClock.Elapsed); PumpPreview(); };
        _preview.Closed += () =>
        {
            _reveal.SetPanelOpen("preview", false);
            _previewHover.Reset(); // closed by itself (last window gone): hovering the app again reopens it
        };
        _dock.ItemContext += OnItemContext;
        _dock.TrayClicked += OnTrayClicked;
        // A dock popup left open would sit on top of Windows' overflow flyout: close it first.
        _dock.OverflowClicked += () => { ClosePopup(); ClosePreview(); OverflowOpener.Open(); };
        _dock.StartClicked += ClosePreview;
        _dock.SearchClicked += ClosePreview;
        _dock.StartContext += OnStartContext;
        _dock.PlacementChanged += _appBar.Refresh;
        _appBar.GrantedChanged += g =>
        {
            void Place() => _dock.SetReservedBottom(g == default ? null : g.Bottom);
            if (_dock.Dispatcher.CheckAccess()) Place();
            else { try { _dock.Dispatcher.BeginInvoke(Place); } catch (Exception) { /* shutting down */ } }
        };
    }

    // The tray opens NotWin's quick-settings panel (App wires TrayClicked to it); close any dock menu first.
    // The tray panel, the launcher and the start-button menu never open under a preview card (review minor, Plan 7).
    private void OnTrayClicked()
    {
        ClosePopup();
        ClosePreview();
    }

    public void Start()
    {
        ApplyKeepVisible(_settings.KeepDockVisible);
        _tracker.Start();
        Rebuild();
    }

    private void Rebuild()
    {
        var items = DockItemsBuilder.Build(_settings.PinnedApps, _tracker.Current);

        // Sync in place: drop vanished, update kept, insert new, then fix order.
        var wanted = new HashSet<string>(items.Select(i => i.AppId), StringComparer.OrdinalIgnoreCase);
        for (var i = _vm.Items.Count - 1; i >= 0; i--)
            if (!wanted.Contains(_vm.Items[i].AppId)) _vm.Items.RemoveAt(i);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var icon = IconFor(item);
            var at = FindIndex(item.AppId, i);
            if (at < 0)
            {
                _vm.Items.Insert(i, new DockItemViewModel(item, icon));
            }
            else
            {
                _vm.Items[at].Update(item, icon);
                if (at != i) _vm.Items.Move(at, i);
            }
        }

        LogItems(items);
    }

    private int FindIndex(string appId, int from)
    {
        for (var j = from; j < _vm.Items.Count; j++)
            if (string.Equals(_vm.Items[j].AppId, appId, StringComparison.OrdinalIgnoreCase)) return j;
        return -1;
    }

    private System.Windows.Media.ImageSource IconFor(DockItem item)
    {
        var exe = item.Windows.Count > 0 ? item.Windows[0].ExePath : item.LaunchPath;
        // Packaged apps (AUMID "family!app") get their package logo through shell:AppsFolder.
        return IsUwpId(item.AppId)
            ? _icons.Get(item.AppId, exe, IconPx)
            : _icons.Get(exe ?? item.AppId, IconPx);
    }

    private void LogItems(IReadOnlyList<DockItem> items)
    {
        var line = string.Join("; ", items.Select(i =>
        {
            var s = $"{i.AppId}[{(i.Pinned ? "P" : "")}{i.Windows.Count}{(i.Focused ? "F" : "")}]";
            if (_trace) s += "{" + string.Join("|", i.Windows.Select(w => Trunc(w.Title, 24))) + "}";
            return s;
        }));
        if (line == _lastLog) return;
        _lastLog = line;
        Log.Info($"dock items ({items.Count}): {line}");
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "~";

    // ---- window previews (Plan 7) ----

    private readonly WindowPreviewCard _preview = new();
    private readonly PreviewHover _previewHover = new();
    private readonly System.Diagnostics.Stopwatch _previewClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly System.Windows.Threading.DispatcherTimer _previewTimer = new();
    private string? _hoverItem;

    /// <summary>The "Window previews in the dock" switch.</summary>
    public bool PreviewsEnabled
    {
        get => DockItemViewModel.PreviewsEnabled;
        set
        {
            DockItemViewModel.PreviewsEnabled = value;
            foreach (var vm in _vm.Items) vm.Update(vm.Item, vm.Icon);
            if (!value) ClosePreview();
        }
    }

    private void OnItemHover(DockItemViewModel? vm, FrameworkElement? el)
    {
        _hoverItem = vm is { Running: true } && PreviewsEnabled ? vm.AppId : null;
        var over = DockWindowNative.GetCursorPos() is { } c && _preview.IsOver(c);
        _previewHover.Pointer(_hoverItem, over, _previewClock.Elapsed);
        PumpPreview();
    }

    private void PumpPreview()
    {
        try
        {
            if (_previewHover.Update(_previewClock.Elapsed)) ApplyPreview();
            _previewTimer.Stop();
            if (_previewHover.NextCheckAt is { } at)
            {
                var wait = at - _previewClock.Elapsed;
                _previewTimer.Interval = (wait > TimeSpan.Zero ? wait : TimeSpan.Zero) + TimeSpan.FromMilliseconds(5);
                _previewTimer.Start();
            }
        }
        catch (Exception e) { Log.Error("dock preview failed", e); }
    }

    private void ApplyPreview()
    {
        var id = _previewHover.OpenFor;
        var vm = id is null ? null : _vm.Items.FirstOrDefault(v => v.AppId == id);
        if (vm is null || !vm.Running || _dock.IconRect(vm.AppId) is not { } rect)
        {
            if (_preview.IsOpen) _preview.Close();
            return;
        }
        ClosePopup();
        _reveal.SetPanelOpen("preview", true); // keeps the dock up while the card is open
        _preview.Show(vm.Item.Windows, rect, _dock.Handle);
    }

    private void RefreshPreview()
    {
        if (!_preview.IsOpen || _previewHover.OpenFor is not { } id) return;
        try
        {
            var vm = _vm.Items.FirstOrDefault(v => v.AppId == id);
            _preview.Refresh(vm?.Item.Windows ?? []);
        }
        catch (Exception e) { Log.Error("dock preview refresh failed", e); }
    }

    private void ClosePreview()
    {
        if (_preview.IsOpen) _preview.Close();
        _previewHover.Close(); // after Close: a click keeps it shut until the pointer leaves the icon
    }

    private void OnItemClicked(DockItemViewModel vm, FrameworkElement el)
    {
        ClosePreview();
        ClosePopup();
        var item = vm.Item;
        try
        {
            switch (DockItemsBuilder.DecideClick(item))
            {
                case DockClickAction.Launch: Launch(item); break;
                case DockClickAction.Activate: Activate(item.Windows[0]); break;
                case DockClickAction.Minimize: WindowListNative.Minimize(item.Windows[0].Handle); break;
                case DockClickAction.ShowWindowList:
                    ShowPopup(el, item.Windows.Select(w => new PopupRow(w.Title, () => Activate(w))).ToList());
                    break;
            }
        }
        catch (Exception e) { Log.Error("dock click failed", e); }
    }

    private void OnItemContext(DockItemViewModel vm, FrameworkElement el)
    {
        ClosePreview();
        ClosePopup();
        var item = vm.Item;
        var rows = new List<PopupRow>
        {
        };
        if (item.Pinned) rows.Add(new PopupRow("Unpin from dock", () => Unpin(item)));
        else if (CanPin(item)) rows.Add(new PopupRow("Pin to dock", () => Pin(item)));
        if (item.Running)
            rows.Add(new PopupRow(item.Windows.Count > 1 ? "Close all windows" : "Close window", () =>
            {
                foreach (var w in item.Windows) WindowListNative.RequestClose(w.Handle);
            }));
        ShowPopup(el, rows);
    }

    private void ShowPopup(FrameworkElement anchor, List<PopupRow> rows)
    {
        var popup = new DockPopup(rows);
        _popup = popup;
        popup.Dismissed += () =>
        {
            if (ReferenceEquals(_popup, popup)) _popup = null;
            _reveal.SetPanelOpen("popup", false);
        };
        _reveal.SetPanelOpen("popup", true); // keeps the dock up while the popup is open
        var p = _dock.Placement;
        var top = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
        popup.ShowAbove(_dock.Handle, top.X, p.Pill.Top - 8 * (p.Dpi / 96.0), p.Monitor);
    }

    // Interim settings menu (right-click on the start button) until the Plan 3 notch provides Settings.
    private void OnStartContext(FrameworkElement anchor)
    {
        ClosePopup();
        ClosePreview();
        var keep = _settings.KeepDockVisible;
        var start = _settings.StartWithWindows;
        ShowPopup(anchor,
        [
            new PopupRow("Settings…", () => SettingsRequested?.Invoke()),
            new PopupRow("Add a program…", () => AddProgramRequested?.Invoke()),
            new PopupRow("Keep taskbar visible", () => SetKeepVisible(!keep), keep),
            new PopupRow("Start with Windows", () => SetStartWithWindows(!start), start),
            new PopupRow("Quit NotWin", _quit),
        ]);
    }

    /// <summary>The current settings (this controller owns settings.json; other parts update through <see cref="UpdateSettings"/>).</summary>
    public ShellSettings Settings => _settings;

    /// <summary>
    /// Settings from another PC (Plan 9 sync, already merged and saved): taken over as they are, the dock's pins,
    /// keep-visible and previews re-applied.
    /// </summary>
    public void ApplyExternal(ShellSettings settings)
    {
        _settings = settings;
        Rebuild();
        ApplyKeepVisible(settings.KeepDockVisible);
        PreviewsEnabled = settings.WindowPreviews;
    }

    /// <summary>Applies <paramref name="change"/> and saves, so there is only one writer of settings.json.</summary>
    public void UpdateSettings(Func<ShellSettings, ShellSettings> change)
    {
        _settings = change(_settings);
        try { _store.Save(_settings); }
        catch (Exception e) { Log.Error("saving settings failed", e); }
    }

    /// <summary>"Settings…" in the start button's right-click menu.</summary>
    public event Action? SettingsRequested;

    /// <summary>"Add a program…" in the start button's menu (Plan 14): add an off-index program and pin it to the dock.</summary>
    public event Action? AddProgramRequested;

    /// <summary>Pins a program the user added from their PC (Plan 14): a full path, launched directly like any pinned exe.</summary>
    public void PinProgram(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (_settings.PinnedApps.Any(p => DockItemsBuilder.SameApp(p.AppId, path))) { Rebuild(); return; }
        var pinned = _settings.PinnedApps.Append(new PinnedApp(path, name, path, path.ToLowerInvariant())).ToList();
        SavePins(pinned);
    }

    public void SetKeepVisible(bool on)
    {
        _settings = _settings with { KeepDockVisible = on };
        try { _store.Save(_settings); }
        catch (Exception e) { Log.Error("saving keep-visible failed", e); }
        ApplyKeepVisible(on);
    }

    public void SetStartWithWindows(bool on)
    {
        _settings = _settings with { StartWithWindows = on };
        try { _store.Save(_settings); }
        catch (Exception e) { Log.Error("saving start-with-windows failed", e); }
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe)) Startup.StartupRegistration.SetEnabled(on, exe);
        }
        catch (Exception e) { Log.Error("start-with-windows registry update failed", e); }
    }

    /// <summary>
    /// Keep-visible: the dock stays up on the desktop and over normal windows, and slides down for maximized and
    /// full-screen apps. Nothing is reserved any more (user, 2026-11-21: a maximized app gets the whole screen height),
    /// so a strip reserved by an older build is given back.
    /// </summary>
    private void ApplyKeepVisible(bool on)
    {
        _reveal.SetKeepVisible(on);
        _appBar.Unregister();
    }

    private void ClosePopup() => _popup?.CloseIfOpen();

    /// <summary>Closes any open dock popup (e.g. when a full-screen app comes to the front).</summary>
    public void ClosePopups() => ClosePopup();

    private static void Activate(AppWindow w)
    {
        if (!WindowListNative.Activate(w.Handle))
            Log.Info($"SetForegroundWindow refused for {w.AppId}");
    }

    private static void Launch(DockItem item)
    {
        var path = item.LaunchPath;
        if (string.IsNullOrEmpty(path)) { Log.Info($"launch: no launch path for {item.AppId}"); return; }
        // A Store app pinned by its exe inside WindowsApps cannot be run directly: start it by its app id instead.
        if (PackagedApps.Resolve(path, StoreApps.ReadText, StoreApps.CurrentRoot) is { } packaged) path = packaged;
        // ShellExecuteEx with ASYNCOK: returns immediately; shortcuts, exes and shell:AppsFolder\<AUMID> all work.
        if (!ShellLinkNative.Open(path)) Log.Info($"launch failed error={ShellLinkNative.LastError}"); // never the path (privacy)
    }

    private void Pin(DockItem item)
    {
        if (_settings.PinnedApps.Any(p => DockItemsBuilder.SameApp(p.AppId, item.AppId))) return;
        var exe = item.Windows.Count > 0 ? item.Windows[0].ExePath : item.LaunchPath ?? "";
        var isUwp = IsUwpId(item.AppId);
        var hasExe = !string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe);
        if (!isUwp && !hasExe) { Log.Info($"pin: nothing to launch for {item.AppId}"); return; }

        var launch = isUwp ? @"shell:AppsFolder\" + item.AppId : PackagedApps.Resolve(exe, StoreApps.ReadText, StoreApps.CurrentRoot) ?? exe;
        var name = PinRules.Name(item.AppId, _indexedName?.Invoke(item.AppId), hasExe ? ExeDescription(exe) : null, item.DisplayName);
        var pinned = _settings.PinnedApps.Append(new PinnedApp(item.AppId, name, launch, hasExe && !isUwp ? exe.ToLowerInvariant() : null)).ToList(); // no exe key for UWP: the exe may be a shared host
        SavePins(pinned);
    }

    private static bool IsUwpId(string id) => PinRules.IsPackagedAppId(id);

    private static bool CanPin(DockItem item) =>
        IsUwpId(item.AppId) ||
        (item.Windows.Count > 0 && !string.IsNullOrEmpty(item.Windows[0].ExePath) && System.IO.File.Exists(item.Windows[0].ExePath));

    private static string? ExeDescription(string exe)
    {
        try { return FileVersionInfo.GetVersionInfo(exe).FileDescription; }
        catch { return null; /* caller falls back to the window's name */ }
    }

    private void Unpin(DockItem item) =>
        SavePins(_settings.PinnedApps.Where(p => !DockItemsBuilder.SameApp(p.AppId, item.AppId)).ToList()); // every version's pin

    private void SavePins(List<PinnedApp> pinned)
    {
        _settings = _settings with { PinnedApps = pinned };
        try { _store.Save(_settings); }
        catch (Exception e) { Log.Error("saving pins failed", e); }
        Rebuild();
    }

    public void Dispose()
    {
        ClosePopup();
        _tracker.Dispose();
    }
}
