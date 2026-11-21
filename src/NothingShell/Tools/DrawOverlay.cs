using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using NothingShell.Core;
using NothingShell.Native;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Tools;

/// <summary>
/// Draw (user request 2026-11-19): draw with a pen on a frozen picture of the whole primary screen. Toolbar at the top
/// centre: red / white / black, undo, clear, Done, ✕. Done returns the screen with the drawing at full pixel resolution
/// (the caller copies it and saves a PNG); Esc or ✕ cancels. Ctrl+Z undoes, Enter is Done.
/// </summary>
public sealed class DrawOverlay : Window
{
    private static readonly Color[] Pens = [Color.FromRgb(0xD7, 0x19, 0x21), Color.FromRgb(0xE8, 0xE8, 0xE8), Color.FromRgb(0x11, 0x11, 0x11)];
    private readonly BitmapSource _shot;
    private readonly RectI _bounds;
    private readonly double _scale;
    private readonly InkCanvas _ink = new() { Background = Brushes.Transparent, Cursor = Cursors.Pen };
    private readonly StackPanel _swatches = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TaskCompletionSource<BitmapSource?> _done = new();

    private DrawOverlay(BitmapSource shot, RectI bounds, double scale)
    {
        _shot = shot;
        _bounds = bounds;
        _scale = scale;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Title = "Nothing Shell draw";
        var w = (bounds.Right - bounds.Left) / scale;
        var h = (bounds.Bottom - bounds.Top) / scale;
        _ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Pens[0], Width = 4, Height = 4, FitToCurve = true, IgnorePressure = false, StylusTip = StylusTip.Ellipse,
        };
        var surface = new Grid { Width = w, Height = h };
        surface.Children.Add(new Image { Source = shot, Stretch = Stretch.Fill, Width = w, Height = h });
        surface.Children.Add(_ink);
        var root = new Grid();
        root.Children.Add(surface);
        root.Children.Add(Toolbar());
        Content = root;
        PaintSwatches();

        SourceInitialized += (_, _) => DockWindowNative.PlaceTopmost(new WindowInteropHelper(this).Handle, bounds);
        Loaded += (_, _) => { ForegroundNative.Activate(new WindowInteropHelper(this).Handle); Activate(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Finish(null); }
            else if (e.Key == Key.Enter) { e.Handled = true; Done(); }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Undo(); }
        };
    }

    /// <summary>Captures the primary monitor and lets the user draw; the finished picture, or null when cancelled.</summary>
    public static Task<BitmapSource?> DrawAsync()
    {
        nint hbmp = 0;
        try
        {
            (hbmp, var bounds) = ScreenCaptureNative.CapturePrimary();
            var shot = Imaging.CreateBitmapSourceFromHBitmap(hbmp, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            shot.Freeze();
            var (_, dpi) = DockWindowNative.GetPrimaryMonitor();
            var overlay = new DrawOverlay(shot, bounds, dpi / 96.0);
            overlay.Show();
            return overlay._done.Task;
        }
        catch (Exception e)
        {
            Log.Error("draw: screen capture failed", e);
            return Task.FromResult<BitmapSource?>(null);
        }
        finally { ScreenCaptureNative.Delete(hbmp); }
    }

    /// <summary>
    /// The screenshot with the strokes drawn on top, at the screenshot's pixel size. Strokes are in DIPs and are scaled
    /// by <paramref name="scale"/> (the monitor's DPI scale) so they land where they were drawn.
    /// </summary>
    public static BitmapSource Compose(BitmapSource shot, StrokeCollection strokes, double scale)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(shot, new Rect(0, 0, shot.PixelWidth, shot.PixelHeight));
            dc.PushTransform(new ScaleTransform(scale, scale));
            strokes.Draw(dc);
            dc.Pop();
        }
        var result = new RenderTargetBitmap(shot.PixelWidth, shot.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    // Pill toolbar (Nothing style): colour dots | undo | clear | Done | ✕.
    private FrameworkElement Toolbar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(_swatches);
        bar.Children.Add(Separator());
        bar.Children.Add(TextButton("UNDO", Undo));
        bar.Children.Add(TextButton("CLEAR", () => _ink.Strokes.Clear()));
        bar.Children.Add(Separator());
        var done = new Border
        {
            Background = TileChrome.Text, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 6, 14, 6),
            Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Done", FontFamily = TileChrome.Font, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TileChrome.Dark },
        };
        done.MouseLeftButtonUp += (_, e) => { e.Handled = true; Done(); };
        bar.Children.Add(done);
        var close = TextButton("✕", () => Finish(null));
        close.Margin = new Thickness(10, 0, 2, 0);
        bar.Children.Add(close);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x14, 0x14, 0x14)), CornerRadius = new CornerRadius(24),
            BorderBrush = TileChrome.Edge, BorderThickness = new Thickness(1), Padding = new Thickness(14, 8, 10, 8),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 16, 0, 0),
            Child = bar, Cursor = Cursors.Arrow,
        };
    }

    private void PaintSwatches()
    {
        _swatches.Children.Clear();
        foreach (var color in Pens)
        {
            var selected = _ink.DefaultDrawingAttributes.Color == color;
            var dot = new Ellipse
            {
                Width = 18, Height = 18, Fill = new SolidColorBrush(color),
                Stroke = selected ? TileChrome.Text : TileChrome.Frozen(0x44, 0x44, 0x44), StrokeThickness = selected ? 2 : 1,
            };
            var hit = new Border { Background = Brushes.Transparent, Padding = new Thickness(4), Cursor = Cursors.Hand, Child = dot };
            var c = color;
            hit.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _ink.DefaultDrawingAttributes.Color = c;
                PaintSwatches();
            };
            _swatches.Children.Add(hit);
        }
    }

    private static FrameworkElement Separator() =>
        new Border { Width = 1, Height = 20, Background = TileChrome.Frozen(0x33, 0x33, 0x33), Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };

    private static FrameworkElement TextButton(string text, Action act)
    {
        var t = new TextBlock
        {
            Text = text, FontFamily = TileChrome.Mono, FontSize = 11, Foreground = TileChrome.Dim, Background = Brushes.Transparent,
            Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0),
        };
        t.MouseLeftButtonUp += (_, e) => { e.Handled = true; act(); };
        return t;
    }

    private void Undo()
    {
        if (_ink.Strokes.Count > 0) _ink.Strokes.RemoveAt(_ink.Strokes.Count - 1);
    }

    private void Done()
    {
        try { Finish(Compose(_shot, _ink.Strokes, _scale)); }
        catch (Exception e)
        {
            Log.Error("draw: composing the picture failed", e);
            Finish(null);
        }
    }

    private void Finish(BitmapSource? result)
    {
        if (_done.Task.IsCompleted) return;
        _done.TrySetResult(result);
        Close();
    }
}
