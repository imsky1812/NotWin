using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NotWin.Core;
using NotWin.Core.Launcher;
using NotWin.Core.Widgets;
using NotWin.Dock;
using NotWin.Native;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// App folder (Plan 5b design §5.4): the card shows only a dot picture (no name). A click opens a dock-like popup
/// with the apps' icons (a row wraps after 8) and a last ＋ tile: click an icon to open the app, right-click →
/// "Remove from folder", ＋ → the app chooser (search over the launcher's installed apps). Apps are stored as
/// launcher ids; uninstalled ones are skipped. App names are never logged.
/// </summary>
public sealed class FolderWidget : IDesktopWidget
{
    public const int PerRow = 8;
    private readonly string _id;
    private readonly Func<IReadOnlyList<LaunchItem>> _apps;
    private readonly Func<string, LaunchItem?> _find;
    private readonly AppIconCache _icons;
    private readonly Action<WidgetConfig> _save;
    private WidgetConfig _config;
    private RectI _screen;
    private FolderPopup? _popup;
    private AppChooser? _chooser;

    public FolderWidget(string id, WidgetConfig? config, Func<IReadOnlyList<LaunchItem>> apps, Func<string, LaunchItem?> find,
        AppIconCache icons, Action<WidgetConfig> save)
    {
        _id = id;
        _apps = apps;
        _find = find;
        _icons = icons;
        _save = save;
        _config = config ?? new WidgetConfig();
    }

    public WidgetKind Kind => WidgetKind.Folder;

    private IReadOnlyList<string> AppIds => _config.Apps ?? [];

    public void Configure(WidgetConfig? config) => _config = config ?? new WidgetConfig();

    public FrameworkElement Build(WidgetSize size)
    {
        var dot = size.W >= 2 ? 15.0 : 7.0;
        var art = new DotArtView
        {
            Rows = DotArt.Rows(_config.Picture), Dot = dot, Gap = Math.Round(dot * 0.35, 1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var root = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { art } };
        root.MouseLeftButtonUp += (_, e) => { e.Handled = true; Guard(Open); };
        return root;
    }

    public void Placed(RectI screen, RectI monitor) => _screen = screen;

    private IReadOnlyList<LaunchItem> Items => FolderApps.Resolve(AppIds, _find);

    private void Open()
    {
        _popup?.CloseIfOpen();
        _popup = FolderPopup.Show(BuildPopupContent(Items, _icons, Launch, Remove, Choose), _screen);
    }

    private void Launch(LaunchItem item)
    {
        _popup?.CloseIfOpen();
        var ok = ShellLinkNative.Open(item.Target);
        Log.Info($"folder launch ok={ok}"); // never the app's name
    }

    private void Remove(LaunchItem item)
    {
        _config = _config with { Apps = FolderApps.Remove(AppIds, item.Id) };
        _save(_config);
    }

    private void Choose()
    {
        _popup?.CloseIfOpen();
        _chooser?.CloseIfOpen();
        _chooser = AppChooser.Show(_apps(), _icons, _screen, item =>
        {
            _config = _config with { Apps = FolderApps.Add(AppIds, item.Id) };
            _save(_config);
        });
    }

    /// <summary>The popup's rows: app tiles (Tag = the <see cref="LaunchItem"/>) then the ＋ tile (Tag = "add"), 8 per row.</summary>
    public static StackPanel BuildPopupContent(IReadOnlyList<LaunchItem> items, AppIconCache icons,
        Action<LaunchItem> open, Action<LaunchItem> remove, Action add)
    {
        var tiles = new List<FrameworkElement>();
        foreach (var item in items) tiles.Add(AppTile(item, icons, open, remove));
        tiles.Add(AddTile(add));
        var rows = new StackPanel();
        for (var i = 0; i < tiles.Count; i += PerRow)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var t in tiles.Skip(i).Take(PerRow)) row.Children.Add(t);
            rows.Children.Add(row);
        }
        return rows;
    }

    private static FrameworkElement AppTile(LaunchItem item, AppIconCache icons, Action<LaunchItem> open, Action<LaunchItem> remove)
    {
        ImageSource? icon = null;
        try { icon = icons.Get(item.Id, 40); }
        catch (Exception e) { Log.Info($"folder icon failed: {e.GetType().Name}"); } // no app paths in the log
        var tile = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(14), Background = Brushes.Transparent, Cursor = Cursors.Hand,
            ToolTip = item.Name, Tag = item,
            Child = new Image { Source = icon, Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        tile.MouseEnter += (_, _) => tile.Background = TileChrome.Frozen(0x2A, 0x2A, 0x2A);
        tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
        tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; Guard(() => open(item)); };
        tile.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            Guard(() =>
            {
                if (DockWindowNative.GetCursorPos() is not { } c) return;
                var (mon, _) = DockWindowNative.GetPrimaryMonitor();
                new DockPopup([new PopupRow("Remove from folder", () => Guard(() => remove(item)))]).ShowAbove(0, c.X, c.Y - 4, mon);
            });
        };
        return tile;
    }

    private static FrameworkElement AddTile(Action add)
    {
        var tile = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand, Tag = "add",
            BorderBrush = TileChrome.FrozenArgb(0x66, 0xE8, 0xE8, 0xE8), BorderThickness = new Thickness(1.5), Background = Brushes.Transparent,
            Child = new TextBlock { Text = "＋", FontSize = 22, Foreground = TileChrome.Text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; Guard(add); };
        return tile;
    }

    internal static void Guard(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Info($"folder action failed: {e.GetType().Name}"); } // never crash the shell, no names
    }

    public void Dispose()
    {
        _popup?.CloseIfOpen();
        _chooser?.CloseIfOpen();
    }
}

/// <summary>A borderless topmost popup that closes on a click outside (deactivation) or Esc; shared by the folder's windows.</summary>
public abstract class WidgetPopupWindow : Window
{
    private bool _closed;

    protected WidgetPopupWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        Deactivated += (_, _) => CloseIfOpen();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CloseIfOpen(); };
    }

    /// <summary>Shows the window above <paramref name="anchor"/> (below when there is no room), clamped to the monitor.</summary>
    protected void ShowNear(RectI anchor)
    {
        Opacity = 0;
        Left = anchor.Left;
        Top = anchor.Top;
        Show();
        UpdateLayout();
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var w = (int)Math.Ceiling(ActualWidth * scale);
        var h = (int)Math.Ceiling(ActualHeight * scale);
        var gap = (int)Math.Round(10 * scale);
        var (mon, _) = DockWindowNative.GetPrimaryMonitor();
        var left = Math.Clamp((anchor.Left + anchor.Right) / 2 - w / 2, mon.Left, Math.Max(mon.Left, mon.Right - w));
        var top = anchor.Top - gap - h >= mon.Top ? anchor.Top - gap - h : Math.Min(anchor.Bottom + gap, Math.Max(mon.Top, mon.Bottom - h));
        DockWindowNative.PlaceTopmost(new WindowInteropHelper(this).Handle, new RectI(left, top, left + w, top + h));
        Opacity = 1;
        Activate();
    }

    public void CloseIfOpen()
    {
        if (_closed) return;
        _closed = true;
        Dispatcher.BeginInvoke(() => { try { Close(); } catch (InvalidOperationException) { } });
    }
}

/// <summary>The folder's popup: a rounded black pill like the dock, holding the app tiles.</summary>
public sealed class FolderPopup : WidgetPopupWindow
{
    private FolderPopup(FrameworkElement content) =>
        Content = new Border
        {
            Background = WidgetCard.CardBrush, BorderBrush = WidgetCard.EdgeBrush, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(26), Padding = new Thickness(10), Child = content,
        };

    public static FolderPopup Show(FrameworkElement content, RectI widget)
    {
        var popup = new FolderPopup(content);
        popup.ShowNear(widget);
        return popup;
    }
}

/// <summary>The folder's app chooser: a search box over the installed apps; a click adds one (the window stays open).</summary>
public sealed class AppChooser : WidgetPopupWindow
{
    /// <summary>At most this many rows: each row loads an icon on the UI thread, so typing stays cheap.</summary>
    public const int MaxRows = 40;

    /// <summary>The apps whose name contains <paramref name="query"/> (case-insensitive), at most <see cref="MaxRows"/>.</summary>
    public static IReadOnlyList<LaunchItem> Matches(IReadOnlyList<LaunchItem> apps, string query)
    {
        var q = query.Trim();
        return apps.Where(a => q.Length == 0 || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(MaxRows).ToList();
    }

    private readonly IReadOnlyList<LaunchItem> _apps;
    private readonly AppIconCache _icons;
    private readonly Action<LaunchItem> _pick;
    private readonly StackPanel _list = new();
    private readonly TextBox _search;

    private AppChooser(IReadOnlyList<LaunchItem> apps, AppIconCache icons, Action<LaunchItem> pick)
    {
        _apps = apps;
        _icons = icons;
        _pick = pick;
        _search = new TextBox
        {
            FontFamily = TileChrome.Font, FontSize = 14, Foreground = TileChrome.Text, CaretBrush = TileChrome.Text,
            Background = TileChrome.Frozen(0x26, 0x26, 0x26), BorderThickness = new Thickness(0), Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8),
        };
        _search.TextChanged += (_, _) => Fill();
        Content = new Border
        {
            Background = WidgetCard.CardBrush, BorderBrush = WidgetCard.EdgeBrush, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(20), Padding = new Thickness(10), Width = 320,
            Child = new DockPanel
            {
                LastChildFill = true,
                Children = { DockTop(_search), new ScrollViewer { Height = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list } },
            },
        };
        Fill();
    }

    private static UIElement DockTop(UIElement e)
    {
        DockPanel.SetDock(e, System.Windows.Controls.Dock.Top);
        return e;
    }

    public static AppChooser Show(IReadOnlyList<LaunchItem> apps, AppIconCache icons, RectI widget, Action<LaunchItem> pick)
    {
        var chooser = new AppChooser(apps, icons, pick);
        chooser.ShowNear(widget);
        chooser._search.Focus();
        return chooser;
    }

    private void Fill()
    {
        _list.Children.Clear();
        foreach (var app in Matches(_apps, _search.Text))
        {
            ImageSource? icon = null;
            try { icon = _icons.Get(app.Id, 24); }
            catch (Exception e) { Log.Info($"chooser icon failed: {e.GetType().Name}"); }
            var row = new Border
            {
                Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new Image { Source = icon, Width = 24, Height = 24, Margin = new Thickness(0, 0, 10, 0) },
                        new TextBlock { Text = app.Name, FontFamily = TileChrome.Font, FontSize = 14, Foreground = TileChrome.Text, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            var item = app;
            row.MouseEnter += (_, _) => row.Background = TileChrome.Frozen(0x2A, 0x2A, 0x2A);
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                FolderWidget.Guard(() => _pick(item));
                row.Opacity = 0.4; // added; the chooser stays open for more
            };
            _list.Children.Add(row);
        }
    }
}
