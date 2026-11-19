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
            var rows = OverlayOpen ? _overlayRows : Math.Max(1, GridPacker.Rows(Sizes()));
            return Pad + rows * Cell + (rows - 1) * Gap + FooterHeight + Pad;
        }
    }

    /// <summary>
    /// The cached view for <paramref name="kind"/>. A tile that fails to construct becomes a placeholder: one broken tile
    /// (e.g. bad cached data) must never take the shell down on hover (final review #5).
    /// </summary>
    public ITileView View(TileKind kind)
    {
        if (_views.TryGetValue(kind, out var v)) return v;
        try { v = _factory(kind); }
        catch (Exception e)
        {
            Log.Error($"notch tile {kind} failed to start", e);
            v = new PlaceholderTile(kind);
        }
        _views[kind] = v;
        return v;
    }

    private FrameworkElement BuildSafe(TileSpec spec)
    {
        try { return View(spec.Kind).Build(spec.Size); }
        catch (Exception e)
        {
            Log.Error($"notch tile {spec.Kind} failed to draw", e);
            return new PlaceholderTile(spec.Kind).Build(spec.Size);
        }
    }

    private void Safe(TileKind kind, Action<ITileView> act)
    {
        try { act(View(kind)); }
        catch (Exception e) { Log.Error($"notch tile {kind} failed", e); }
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
        foreach (var k in Visible()) Safe(k, v => { if (open) v.Opened(); else v.Closed(); });
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
        foreach (var k in before.Except(after)) Safe(k, v => v.Closed());
        foreach (var k in after.Except(before)) Safe(k, v => v.Opened());
    }

    private void Render()
    {
        _grid.Children.Clear();
        var tiles = _layout.Pages[_page];
        var sizes = Sizes();
        var placed = GridPacker.Pack(sizes);
        var rows = 0;
        for (var i = 0; i < sizes.Count; i++)
        {
            var (w, h) = TileRules.Cells(sizes[i]);
            FrameworkElement el;
            if (i < tiles.Count)
            {
                el = BuildSafe(tiles[i]);
                if (_editing) el = Decorate(el, i, tiles[i]);
            }
            else el = AddTile();
            el.Width = w * Cell + (w - 1) * Gap;
            el.Height = h * Cell + (h - 1) * Gap;
            Canvas.SetLeft(el, placed[i].Col * (Cell + Gap));
            Canvas.SetTop(el, placed[i].Row * (Cell + Gap));
            _grid.Children.Add(el);
            if (i < tiles.Count) TilePlaced?.Invoke(el, i);
            rows = Math.Max(rows, placed[i].Row + h);
        }
        rows = Math.Max(1, rows);
        _grid.Width = GridPacker.Columns * Cell + (GridPacker.Columns - 1) * Gap;
        _grid.Height = rows * Cell + (rows - 1) * Gap;
        RenderFooter();
    }

    /// <summary>Edit mode: the size chip on tile <c>index</c> was clicked (cycle its size).</summary>
    public event Action<int>? SizeChipClicked;

    /// <summary>Edit mode: ✕ on tile <c>index</c> was clicked.</summary>
    public event Action<int>? RemoveClicked;

    /// <summary>Edit mode: "＋ Add" was clicked.</summary>
    public event Action? AddClicked;

    // Edit mode (mockup .nediting .t, .nchip, .nx): dashed outline, size chip at the bottom right, red ✕ at the top
    // right; the tile itself ignores clicks while editing.
    private FrameworkElement Decorate(FrameworkElement tile, int index, TileSpec spec)
    {
        tile.IsHitTestVisible = false;
        var outline = new Rectangle
        {
            Stroke = TileChrome.Dim, StrokeThickness = 1.5, StrokeDashArray = [3, 2], RadiusX = 22, RadiusY = 22,
            IsHitTestVisible = false,
        };
        var (cw, ch) = TileRules.Cells(spec.Size);
        var chip = new Border
        {
            Background = TileChrome.Text, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8), Cursor = Cursors.Hand,
            Child = new TextBlock { Text = $"{cw}×{ch} ▸", FontFamily = TileChrome.Mono, FontSize = 10, Foreground = TileChrome.Dark },
        };
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; SizeChipClicked?.Invoke(index); };
        var x = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = TileChrome.Red,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8), Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "✕", FontSize = 10, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        x.MouseLeftButtonUp += (_, e) => { e.Handled = true; RemoveClicked?.Invoke(index); };
        return new Grid { Children = { tile, outline, chip, x } };
    }

    // "＋ ADD" (mockup .add): a dashed, transparent 1x1 that brings back the first removed tile.
    private FrameworkElement AddTile()
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = "＋", FontSize = 18, Foreground = TileChrome.Dim, HorizontalAlignment = HorizontalAlignment.Center,
        });
        var label = TileChrome.Label("Add");
        label.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(label);
        var outline = new Rectangle
        {
            Stroke = TileChrome.Frozen(0x3A, 0x3A, 0x3A), StrokeThickness = 1.5, StrokeDashArray = [3, 2],
            RadiusX = 22, RadiusY = 22, IsHitTestVisible = false,
        };
        var root = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { outline, stack } };
        root.MouseLeftButtonUp += (_, e) => { e.Handled = true; AddClicked?.Invoke(); };
        return root;
    }

    // The page's tile sizes, plus a 1x1 "＋ Add" in edit mode when something was removed and it still fits.
    private List<TileSize> Sizes()
    {
        var tiles = _layout.Pages[_page];
        var sizes = tiles.Select(t => t.Size).ToList();
        if (_editing && _layout.CanAddBack(_page)) sizes.Add(TileSize.One); // only when a click would bring one back
        return sizes;
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
