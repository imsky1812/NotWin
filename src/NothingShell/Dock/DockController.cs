using System.Diagnostics;
using System.Windows;
using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Dock;

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
    private readonly bool _trace = Environment.GetEnvironmentVariable("NOTHINGSHELL_DOCK_TRACE") == "1";
    private ShellSettings _settings;
    private readonly DockAppBar _appBar;
    private readonly Action _quit;
    private DockPopup? _popup;
    private string _lastLog = "";

    public DockController(DockWindow dock, DockViewModel vm, DockReveal reveal, SettingsStore store, ShellSettings settings,
        DockAppBar appBar, Action quit)
    {
        _dock = dock; _vm = vm; _reveal = reveal; _store = store; _settings = settings;
        _appBar = appBar; _quit = quit;
        _tracker = new WindowTracker();
        _tracker.Changed += Rebuild;
        _dock.ItemClicked += OnItemClicked;
        _dock.ItemContext += OnItemContext;
        _dock.TrayClicked += OnTrayClicked;
        _dock.OverflowClicked += OverflowOpener.Open;
        _dock.StartContext += OnStartContext;
        _dock.PlacementChanged += _appBar.Refresh;
        _appBar.GrantedChanged += g =>
        {
            void Place() => _dock.SetReservedBottom(g == default ? null : g.Bottom);
            if (_dock.Dispatcher.CheckAccess()) Place();
            else { try { _dock.Dispatcher.BeginInvoke(Place); } catch (Exception) { /* shutting down */ } }
        };
    }

    // Until the Plan 3 quick-settings panel exists, the tray opens Windows' own (Win+A).
    private void OnTrayClicked()
    {
        ClosePopup();
        if (!TrayInputNative.SendQuickSettings()) Log.Info("tray: Win+A injection failed");
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

    private void OnItemClicked(DockItemViewModel vm, FrameworkElement el)
    {
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
            _reveal.SetPanelOpen(false);
        };
        _reveal.SetPanelOpen(true); // keeps the dock up while the popup is open
        var p = _dock.Placement;
        var top = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0));
        popup.ShowAbove(top.X, p.Pill.Top - 8 * (p.Dpi / 96.0), p.Monitor, p.Dpi);
    }

    // Interim settings menu (right-click on the start button) until the Plan 3 notch provides Settings.
    private void OnStartContext(FrameworkElement anchor)
    {
        ClosePopup();
        var keep = _settings.KeepDockVisible;
        ShowPopup(anchor,
        [
            new PopupRow("Keep taskbar visible", () => SetKeepVisible(!keep), keep),
            new PopupRow("Quit Nothing Shell", _quit),
        ]);
    }

    private void SetKeepVisible(bool on)
    {
        _settings = _settings with { KeepDockVisible = on };
        try { _store.Save(_settings); }
        catch (Exception e) { Log.Error("saving keep-visible failed", e); }
        ApplyKeepVisible(on);
    }

    /// <summary>Keep-visible: the dock stays up and reserves its strip as an appbar; off: it floats and nothing is reserved.</summary>
    private void ApplyKeepVisible(bool on)
    {
        _reveal.SetKeepVisible(on);
        if (on) _appBar.Register(_dock, DockWindowNative.GetPrimaryMonitor().Bounds);
        else _appBar.Unregister();
    }

    private void ClosePopup() => _popup?.CloseIfOpen();

    private static void Activate(AppWindow w)
    {
        if (!WindowListNative.Activate(w.Handle))
            Log.Info($"SetForegroundWindow refused for {w.AppId}");
    }

    private static void Launch(DockItem item)
    {
        var path = item.LaunchPath;
        if (string.IsNullOrEmpty(path)) { Log.Info($"launch: no launch path for {item.AppId}"); return; }
        // ShellExecuteEx with ASYNCOK: returns immediately; shortcuts, exes and shell:AppsFolder\<AUMID> all work.
        if (!ShellLinkNative.Open(path)) Log.Info($"launch failed for {item.AppId} ({path})");
    }

    private void Pin(DockItem item)
    {
        if (_settings.PinnedApps.Any(p => string.Equals(p.AppId, item.AppId, StringComparison.OrdinalIgnoreCase))) return;
        var exe = item.Windows.Count > 0 ? item.Windows[0].ExePath : item.LaunchPath ?? "";
        var isUwp = IsUwpId(item.AppId);
        var hasExe = !string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe);
        if (!isUwp && !hasExe) { Log.Info($"pin: nothing to launch for {item.AppId}"); return; }

        var launch = isUwp ? @"shell:AppsFolder\" + item.AppId : exe;
        var name = hasExe ? FriendlyName(exe, item.DisplayName) : item.DisplayName;
        var pinned = _settings.PinnedApps.Append(new PinnedApp(item.AppId, name, launch, hasExe && !isUwp ? exe.ToLowerInvariant() : null)).ToList(); // no exe key for UWP: the exe may be a shared host
        SavePins(pinned);
    }

    // Packaged-app AUMID "family!app": has '!' and is not a path.
    private static bool IsUwpId(string id) => id.Contains('!') && !id.Contains('\\') && !id.Contains(':');

    private static bool CanPin(DockItem item) =>
        IsUwpId(item.AppId) ||
        (item.Windows.Count > 0 && !string.IsNullOrEmpty(item.Windows[0].ExePath) && System.IO.File.Exists(item.Windows[0].ExePath));

    private static string FriendlyName(string exe, string fallback)
    {
        try
        {
            var d = FileVersionInfo.GetVersionInfo(exe).FileDescription;
            if (!string.IsNullOrWhiteSpace(d)) return d.Trim();
        }
        catch { /* fall back to the exe name */ }
        return fallback;
    }

    private void Unpin(DockItem item) =>
        SavePins(_settings.PinnedApps.Where(p => !string.Equals(p.AppId, item.AppId, StringComparison.OrdinalIgnoreCase)).ToList());

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
