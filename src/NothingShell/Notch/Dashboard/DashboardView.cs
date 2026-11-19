using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NothingShell.Core.Notch;

namespace NothingShell.Notch.Dashboard;

/// <summary>
/// The open notch's content (mockup <c>.l-open</c>, <c>.ngrid</c>, <c>.nfoot</c>): tiles of 84 DIP cells with 10 DIP
/// gaps on 8 columns, packed densely; footer with ✎ Edit on the left and page dots on the right. An overlay (clipboard
/// list, notes editor) can replace the grid. Height = grid + 28 padding + 26 footer (mockup <c>fitN</c>).
/// </summary>
public sealed class DashboardView : IDisposable
{
    public const double Cell = 84, Gap = 10, Pad = 14, FooterHeight = 26;
    public const double Width = Pad * 2 + GridPacker.Columns * Cell + (GridPacker.Columns - 1) * Gap; // 770

    private readonly Func<TileKind, ITileView> _factory;
    private readonly Dictionary<TileKind, ITileView> _views = [];
    private readonly Canvas _grid = new() { ClipToBounds = false };
    private readonly Border _overlay = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _editLabel;
    private readonly FrameworkElement _editIcon;
    private readonly StackPanel _edit;
    private DashboardLayout _layout = DashboardLayout.Default;
    private int _page;
    private bool _editing, _open;
    private int _overlayRows;

    public DashboardView(Func<TileKind, ITileView> factory)
    {
        _factory = factory;
        _editIcon = TileChrome.Icon("pencil", 14, TileChrome.Dim);
        _editIcon.Margin = new Thickness(0, 0, 6, 0);
        _editLabel = new TextBlock
        {
            Text = "EDIT", FontFamily = TileChrome.Mono, FontSize = 10, Foreground = TileChrome.Dim, VerticalAlignment = VerticalAlignment.Center,
        };
        _edit = new StackPanel
        {
            Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = Cursors.Hand,
            Children = { _editIcon, _editLabel },
        };
        _edit.MouseLeftButtonUp += (_, e) => { e.Handled = true; EditToggled?.Invoke(); };

        var footer = new DockPanel { Margin = new Thickness(4, 10, 4, 0), Height = FooterHeight - 10, LastChildFill = false };
        DockPanel.SetDock(_dots, System.Windows.Controls.Dock.Right);
        footer.Children.Add(_dots);
        footer.Children.Add(_edit);

        var content = new Grid { Children = { _grid, _overlay } };
        var root = new DockPanel { Margin = new Thickness(Pad, Pad, Pad, 0) };
        DockPanel.SetDock(footer, System.Windows.Controls.Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(content);
        root.PreviewMouseWheel += (_, e) =>
        {
            if (_overlay.Visibility == Visibility.Visible || _layout.Pages.Count < 2) return;
            var next = Math.Clamp(_page + (e.Delta < 0 ? 1 : -1), 0, _layout.Pages.Count - 1);
            if (next != _page) PageRequested?.Invoke(next);
            e.Handled = true;
        };
        Root = root;
    }

    public FrameworkElement Root { get; }

    public event Action<int>? PageRequested;
    public event Action? EditToggled;

    public int Page => _page;
    public DashboardLayout Layout => _layout;
    public bool OverlayOpen => _overlay.Visibility == Visibility.Visible;

    /// <summary>Pill height for the current content (grid or overlay).</summary>
    public double Height
    {
        get
        {
            var rows = OverlayOpen ? _overlayRows
                : Math.Max(1, GridPacker.Rows(_layout.Pages[_page].Select(t => t.Size).ToList()));
            return Pad + rows * Cell + (rows - 1) * Gap + FooterHeight + Pad;
        }
    }

    public ITileView View(TileKind kind)
    {
        if (!_views.TryGetValue(kind, out var v)) _views[kind] = v = _factory(kind);
        return v;
    }

    public int PageOf(TileKind kind)
    {
        for (var p = 0; p < _layout.Pages.Count; p++)
            if (_layout.Pages[p].Any(t => t.Kind == kind)) return p;
        return -1;
    }

    /// <summary>Lays out <paramref name="page"/>. Tiles leaving the screen get Closed, tiles arriving get Opened.</summary>
    public void Show(DashboardLayout layout, int page, bool editing)
    {
        var before = Visible().ToHashSet();
        _layout = layout;
        _page = Math.Clamp(page, 0, layout.Pages.Count - 1);
        _editing = editing;
        Render();
        if (_open) Switch(before, Visible().ToHashSet());
    }

    /// <summary>The notch opened or closed: starts/stops the visible tiles.</summary>
    public void SetOpen(bool open)
    {
        if (open == _open) return;
        _open = open;
        foreach (var k in Visible())
        {
            if (open) View(k).Opened();
            else View(k).Closed();
        }
        if (!open) CloseOverlay();
    }

    /// <summary>Replaces the grid with <paramref name="content"/> (<paramref name="rows"/> cells tall) until <see cref="CloseOverlay"/>.</summary>
    public void ShowOverlay(FrameworkElement content, int rows = 2)
    {
        _overlayRows = rows;
        _overlay.Child = content;
        _overlay.Height = rows * Cell + (rows - 1) * Gap;
        _overlay.Visibility = Visibility.Visible;
        _grid.Visibility = Visibility.Hidden;
        OverlayChanged?.Invoke();
    }

    public void CloseOverlay()
    {
        if (!OverlayOpen) return;
        _overlay.Child = null;
        _overlay.Visibility = Visibility.Collapsed;
        _grid.Visibility = Visibility.Visible;
        OverlayChanged?.Invoke();
    }

    /// <summary>The overlay opened or closed (the pill height changes).</summary>
    public event Action? OverlayChanged;

    /// <summary>Raised after every render with each tile's element and its index on the page (edit mode decorates them).</summary>
    public event Action<FrameworkElement, int>? TilePlaced;

    private IEnumerable<TileKind> Visible() =>
        _layout.Pages.Count == 0 ? [] : _layout.Pages[Math.Clamp(_page, 0, _layout.Pages.Count - 1)].Select(t => t.Kind);

    private void Switch(HashSet<TileKind> before, HashSet<TileKind> after)
    {
        foreach (var k in before.Except(after)) View(k).Closed();
        foreach (var k in after.Except(before)) View(k).Opened();
    }

    private void Render()
    {
        _grid.Children.Clear();
        var tiles = _layout.Pages[_page];
        var placed = GridPacker.Pack(tiles.Select(t => t.Size).ToList());
        var rows = 0;
        for (var i = 0; i < tiles.Count; i++)
        {
            var (w, h) = TileRules.Cells(tiles[i].Size);
            var el = View(tiles[i].Kind).Build(tiles[i].Size);
            el.Width = w * Cell + (w - 1) * Gap;
            el.Height = h * Cell + (h - 1) * Gap;
            Canvas.SetLeft(el, placed[i].Col * (Cell + Gap));
            Canvas.SetTop(el, placed[i].Row * (Cell + Gap));
            if (_editing) el.IsHitTestVisible = true;
            _grid.Children.Add(el);
            TilePlaced?.Invoke(el, i);
            rows = Math.Max(rows, placed[i].Row + h);
        }
        rows = Math.Max(1, rows);
        _grid.Width = GridPacker.Columns * Cell + (GridPacker.Columns - 1) * Gap;
        _grid.Height = rows * Cell + (rows - 1) * Gap;
        RenderFooter();
    }

    private void RenderFooter()
    {
        _editLabel.Text = _editing ? "DONE" : "EDIT";
        _editLabel.Foreground = _editing ? TileChrome.Text : TileChrome.Dim;
        _dots.Children.Clear();
        for (var p = 0; p < _layout.Pages.Count; p++)
        {
            var page = p;
            var dot = new Ellipse
            {
                Width = 6, Height = 6, Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand,
                Fill = p == _page ? TileChrome.Text : TileChrome.Frozen(0x44, 0x44, 0x44),
            };
            // A wider transparent hit area: 6 DIP dots are hard to click.
            var hit = new Border { Background = Brushes.Transparent, Padding = new Thickness(2, 5, 2, 5), Child = dot };
            hit.MouseLeftButtonUp += (_, e) => { e.Handled = true; PageRequested?.Invoke(page); };
            _dots.Children.Add(hit);
        }
    }

    public void Dispose()
    {
        foreach (var v in _views.Values) v.Dispose();
        _views.Clear();
    }
}
