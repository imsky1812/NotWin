using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NotWin.Core;
using NotWin.Core.Dock;
using NotWin.Native;
using NotWin.Notch.Dashboard;

namespace NotWin.Dock;

/// <summary>One window's preview: its handle, the area its live picture fills, its title and its ✕.</summary>
public sealed record PreviewCell(nint Hwnd, Border Area, TextBlock Title, Border CloseButton);

/// <summary>
/// The dock's window previews (design 2026-11-20): a black card above a running app's icon with a live DWM picture of
/// each window (fit inside 220 × 140, aspect kept) and its title; click a picture to bring that window forward, ✕ to
/// close it. Up to six in a row, then the row scrolls. The window is not layered (DWM pictures need a normal window);
/// Windows 11 rounds its corners. DWM draws the pictures above our content, so the ✕ sits in the title row.
/// </summary>
public sealed class WindowPreviewCard
{
    public const double CellWidth = PreviewRules.MaxW, CellGap = 12, AreaHeight = PreviewRules.MaxH;
    private static readonly Brush CardBrush = TileChrome.Frozen(0x1A, 0x1A, 0x1A), AreaBrush = TileChrome.Frozen(0x0E, 0x0E, 0x0E);
    private readonly List<PreviewCell> _cells = [];
    private readonly List<DwmThumbnail> _thumbs = [];
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private Window? _window;
    private RectI _anchor;

    public event Action<nint>? Activate;
    public event Action<nint>? CloseWindow;
    public event Action? Closed;

    /// <summary>Raised when the pointer enters (true) or leaves (false) the card.</summary>
    public event Action<bool>? PointerOver;

    public IReadOnlyList<PreviewCell> Cells => _cells;

    /// <summary>The card's window while open (tests close it from outside).</summary>
    public Window? WindowForTests => _window;
    public bool IsOpen => _window is not null;

    /// <summary>The widest the row gets before it scrolls: six previews.</summary>
    public double RowMaxWidth => 6 * CellWidth + 5 * CellGap;

    /// <summary>Builds the cells for <paramref name="windows"/> (no window is shown; <see cref="Show"/> does that).</summary>
    public void Build(IReadOnlyList<AppWindow> windows)
    {
        _row.Children.Clear();
        _cells.Clear();
        foreach (var w in windows)
        {
            var hwnd = w.Handle;
            var area = new Border { Width = CellWidth, Height = AreaHeight, CornerRadius = new CornerRadius(10), Background = AreaBrush, Cursor = Cursors.Hand };
            area.MouseLeftButtonUp += (_, e) => { e.Handled = true; Activate?.Invoke(hwnd); };
            var title = new TextBlock
            {
                Text = w.Title, FontFamily = TileChrome.Font, FontSize = 12, Foreground = TileChrome.Text,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            };
            var close = new Border
            {
                // A red dot with a white ✕ (user, 2026-11-21).
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = TileChrome.Red,
                Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Close window",
                Child = new TextBlock { Text = "✕", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            close.MouseLeftButtonUp += (_, e) => { e.Handled = true; CloseWindow?.Invoke(hwnd); };
            DockPanel.SetDock(close, System.Windows.Controls.Dock.Right);
            var titleRow = new DockPanel { Margin = new Thickness(2, 8, 0, 0), Children = { close, title } };
            _row.Children.Add(new StackPanel
            {
                Width = CellWidth, Margin = new Thickness(_row.Children.Count == 0 ? 0 : CellGap, 0, 0, 0),
                Children = { area, titleRow },
            });
            _cells.Add(new PreviewCell(hwnd, area, title, close));
        }
    }

    /// <summary>Shows (or moves) the card above <paramref name="itemRect"/> (screen px) with live pictures.</summary>
    public void Show(IReadOnlyList<AppWindow> windows, RectI itemRect, nint owner)
    {
        _anchor = itemRect;
        Build(windows);
        if (_cells.Count == 0) { Close(); return; }
        if (_window is null)
        {
            var scroller = new ScrollViewer
            {
                Content = _row, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxWidth = RowMaxWidth,
            };
            scroller.ScrollChanged += (_, _) => PlaceThumbs();
            scroller.PreviewMouseWheel += (_, e) => { scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta); e.Handled = true; };
            var w = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
                SizeToContent = SizeToContent.WidthAndHeight, Background = CardBrush, Left = -32000, Top = -32000,
                Content = new Border { Padding = new Thickness(12), BorderBrush = TileChrome.FrozenArgb(0x21, 0xFF, 0xFF, 0xFF), BorderThickness = new Thickness(1.5), Child = scroller },
            };
            // Closed by something else (Windows, the owner going): tidy up and say so, or the dock stays held up.
            w.Closed += (_, _) =>
            {
                if (!ReferenceEquals(_window, w)) return; // Close() did it and has already told everyone
                foreach (var t in _thumbs) t.Dispose();
                _thumbs.Clear();
                _window = null;
                Closed?.Invoke();
            };
            w.MouseEnter += (_, _) => PointerOver?.Invoke(true);
            w.MouseLeave += (_, _) => PointerOver?.Invoke(false);
            new WindowInteropHelper(w) { Owner = owner };
            w.SourceInitialized += (_, _) =>
            {
                var h = new WindowInteropHelper(w).Handle;
                DockWindowNative.AddExStyle(h, 0x08000000 | 0x80); // no-activate, tool window
                DwmThumbnail.RoundCorners(h);
            };
            _window = w;
            w.Show();
        }
        _window.UpdateLayout();
        Position();
        PlaceThumbs();
    }

    /// <summary>The app's windows changed: drop the closed ones (the card closes when none are left).</summary>
    public void Refresh(IReadOnlyList<AppWindow> windows)
    {
        if (windows.Count == 0) { Build([]); Close(); return; }
        if (windows.Select(w => w.Handle).SequenceEqual(_cells.Select(c => c.Hwnd))) return;
        Build(windows);
        if (_window is null) return;
        _window.UpdateLayout();
        Position();
        PlaceThumbs();
    }

    public bool IsOver(PointI p)
    {
        if (_window is null) return false;
        var r = DockWindowNative.GetWindowRect(new WindowInteropHelper(_window).Handle);
        return p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
    }

    public void Close()
    {
        foreach (var t in _thumbs) t.Dispose();
        _thumbs.Clear();
        var w = _window;
        _window = null;
        try { w?.Close(); } catch (InvalidOperationException) { }
        Closed?.Invoke();
    }

    // Centred above the item, inside the monitor, 10 px above the dock.
    private void Position()
    {
        if (_window is null) return;
        var h = new WindowInteropHelper(_window).Handle;
        var scale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
        var wPx = (int)Math.Ceiling(_window.ActualWidth * scale);
        var hPx = (int)Math.Ceiling(_window.ActualHeight * scale);
        var (mon, _) = DockWindowNative.GetPrimaryMonitor();
        var left = Math.Clamp((_anchor.Left + _anchor.Right) / 2 - wPx / 2, mon.Left + 8, Math.Max(mon.Left + 8, mon.Right - wPx - 8));
        var bottom = _anchor.Top - (int)Math.Round(10 * scale);
        DockWindowNative.PlaceTopmost(h, new RectI(left, bottom - hPx, left + wPx, bottom));
    }

    // One live picture per cell, fit inside its area (client px), hidden when scrolled out of view.
    private void PlaceThumbs()
    {
        if (_window is null) return;
        foreach (var t in _thumbs) t.Dispose();
        _thumbs.Clear();
        var h = new WindowInteropHelper(_window).Handle;
        var scale = VisualTreeHelper.GetDpi(_window).DpiScaleX;
        var root = (Visual)_window.Content;
        var viewport = new Rect(0, 0, _window.ActualWidth, _window.ActualHeight);
        foreach (var c in _cells)
        {
            if (DwmThumbnail.Register(h, c.Hwnd) is not { } thumb) continue;
            var (sw, sh) = thumb.SourceSize;
            var (fw, fh) = PreviewRules.Fit(sw, sh);
            var topLeft = c.Area.TransformToAncestor(root).Transform(new Point((CellWidth - fw) / 2, (AreaHeight - fh) / 2));
            var r = new Rect(topLeft, new Size(fw, fh));
            if (!viewport.Contains(r.TopLeft) || !viewport.Contains(r.BottomRight)) { thumb.Dispose(); continue; }
            thumb.Place(new RectI((int)Math.Round(r.Left * scale), (int)Math.Round(r.Top * scale),
                (int)Math.Round(r.Right * scale), (int)Math.Round(r.Bottom * scale)));
            _thumbs.Add(thumb);
        }
    }
}
