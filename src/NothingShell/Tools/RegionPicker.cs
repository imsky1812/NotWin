using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using NothingShell.Core;
using NothingShell.Core.Tools;
using NothingShell.Native;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Tools;

public enum PickMode { Region, Point }

/// <summary>What the user picked: the region (physical pixels, screen coordinates), its pixels, and the colour in Point mode.</summary>
public sealed record PickResult(RectI Rect, BitmapSource Shot, Color? Color);

/// <summary>
/// Full-screen picker over the primary monitor for the notch's tools: shows a frozen screenshot under a 40 % black
/// veil. Region mode: drag a rectangle (shown unveiled, 1 px white outline, "W × H" label). Point mode: a 9×9 px
/// magnifier at 11× follows the cursor with the hex below; a click picks. Esc or right-click cancels.
/// </summary>
public sealed class RegionPicker : Window
{
    private readonly PickMode _mode;
    private readonly BitmapSource _shot;
    private readonly RectI _bounds;
    private readonly double _scale;
    private readonly TaskCompletionSource<PickResult?> _done = new();
    private readonly Canvas _canvas = new();
    private readonly Path _veil = new() { Fill = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle _outline = new() { Stroke = Brushes.White, StrokeThickness = 1, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly TextBlock _label = new() { Foreground = Brushes.White, FontSize = 11, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Border _magnifier = new() { Width = 99, Height = 99, BorderBrush = Brushes.White, BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Image _zoom = new() { Stretch = Stretch.Fill };
    private Point? _start;

    private RegionPicker(PickMode mode, BitmapSource shot, RectI bounds, double scale)
    {
        _mode = mode;
        _shot = shot;
        _bounds = bounds;
        _scale = scale;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        Title = "Nothing Shell picker";
        RenderOptions.SetBitmapScalingMode(_zoom, BitmapScalingMode.NearestNeighbor);
        _magnifier.Child = _zoom;
        _label.FontFamily = TileChrome.Mono;
        var image = new Image { Source = shot, Stretch = Stretch.Fill, Width = Dw, Height = Dh, IsHitTestVisible = false };
        _canvas.Width = Dw;
        _canvas.Height = Dh;
        _canvas.Background = Brushes.Transparent;
        _canvas.Children.Add(image);
        _canvas.Children.Add(_veil);
        _canvas.Children.Add(_outline);
        _canvas.Children.Add(_label);
        _canvas.Children.Add(_magnifier);
        Content = _canvas;
        UpdateVeil(null);

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            DockWindowNative.PlaceTopmost(hwnd, bounds);
        };
        Loaded += (_, _) => ForegroundNative.Activate(new WindowInteropHelper(this).Handle);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(null); };
        MouseRightButtonUp += (_, _) => Finish(null);
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        Deactivated += (_, _) => Finish(null);
    }

    private double Dw => (_bounds.Right - _bounds.Left) / _scale;
    private double Dh => (_bounds.Bottom - _bounds.Top) / _scale;

    /// <summary>Captures the primary monitor and lets the user pick; null when cancelled or capture failed.</summary>
    public static Task<PickResult?> PickAsync(PickMode mode)
    {
        nint hbmp = 0;
        try
        {
            (hbmp, var bounds) = ScreenCaptureNative.CapturePrimary();
            var shot = Imaging.CreateBitmapSourceFromHBitmap(hbmp, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            shot.Freeze();
            var (_, dpi) = DockWindowNative.GetPrimaryMonitor();
            var picker = new RegionPicker(mode, shot, bounds, dpi / 96.0);
            picker.Show();
            return picker._done.Task;
        }
        catch (Exception e)
        {
            Log.Error("screen capture failed", e);
            return Task.FromResult<PickResult?>(null);
        }
        finally { ScreenCaptureNative.Delete(hbmp); }
    }

    private PointI Px(Point dip) => new((int)Math.Round(dip.X * _scale), (int)Math.Round(dip.Y * _scale));

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode == PickMode.Point) { PickPoint(e.GetPosition(_canvas)); return; }
        _start = e.GetPosition(_canvas);
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_canvas);
        if (_mode == PickMode.Point) { ShowMagnifier(p); return; }
        if (_start is not { } s) return;
        var r = ToolMath.Normalize(Px(s), Px(p), new RectI(0, 0, _shot.PixelWidth, _shot.PixelHeight));
        UpdateVeil(r);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == PickMode.Point || _start is not { } s) return;
        ReleaseMouseCapture();
        _start = null;
        var r = ToolMath.Normalize(Px(s), Px(e.GetPosition(_canvas)), new RectI(0, 0, _shot.PixelWidth, _shot.PixelHeight));
        if (r is not { } rect) { UpdateVeil(null); return; } // a click, not a drag: keep picking
        var crop = new CroppedBitmap(_shot, new Int32Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top));
        crop.Freeze();
        Finish(new PickResult(new RectI(rect.Left + _bounds.Left, rect.Top + _bounds.Top, rect.Right + _bounds.Left, rect.Bottom + _bounds.Top), crop, null));
    }

    private void UpdateVeil(RectI? r)
    {
        var full = new RectangleGeometry(new Rect(0, 0, Dw, Dh));
        if (r is not { } px)
        {
            _veil.Data = full;
            _outline.Visibility = _label.Visibility = Visibility.Collapsed;
            return;
        }
        var hole = new Rect(px.Left / _scale, px.Top / _scale, (px.Right - px.Left) / _scale, (px.Bottom - px.Top) / _scale);
        _veil.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(hole));
        Canvas.SetLeft(_outline, hole.Left);
        Canvas.SetTop(_outline, hole.Top);
        _outline.Width = hole.Width;
        _outline.Height = hole.Height;
        _outline.Visibility = Visibility.Visible;
        _label.Text = $"{px.Right - px.Left} × {px.Bottom - px.Top}";
        Canvas.SetLeft(_label, hole.Left);
        Canvas.SetTop(_label, Math.Max(0, hole.Top - 18));
        _label.Visibility = Visibility.Visible;
    }

    private Color ColorAt(PointI px)
    {
        var x = Math.Clamp(px.X, 0, _shot.PixelWidth - 1);
        var y = Math.Clamp(px.Y, 0, _shot.PixelHeight - 1);
        var buf = new byte[4];
        _shot.CopyPixels(new Int32Rect(x, y, 1, 1), buf, 4, 0);
        return Color.FromRgb(buf[2], buf[1], buf[0]); // BGRA
    }

    private void ShowMagnifier(Point p)
    {
        var px = Px(p);
        var x = Math.Clamp(px.X - 4, 0, Math.Max(0, _shot.PixelWidth - 9));
        var y = Math.Clamp(px.Y - 4, 0, Math.Max(0, _shot.PixelHeight - 9));
        _zoom.Source = new CroppedBitmap(_shot, new Int32Rect(x, y, Math.Min(9, _shot.PixelWidth), Math.Min(9, _shot.PixelHeight)));
        var c = ColorAt(px);
        _label.Text = ToolMath.Hex(c.R, c.G, c.B);
        var left = p.X + 20 + 99 > Dw ? p.X - 20 - 99 : p.X + 20;
        var top = p.Y + 20 + 120 > Dh ? p.Y - 20 - 120 : p.Y + 20;
        Canvas.SetLeft(_magnifier, left);
        Canvas.SetTop(_magnifier, top);
        Canvas.SetLeft(_label, left);
        Canvas.SetTop(_label, top + 103);
        _magnifier.Visibility = _label.Visibility = Visibility.Visible;
        _veil.Data = new RectangleGeometry(new Rect(0, 0, Dw, Dh));
    }

    private void PickPoint(Point p)
    {
        var px = Px(p);
        Finish(new PickResult(new RectI(px.X + _bounds.Left, px.Y + _bounds.Top, px.X + _bounds.Left + 1, px.Y + _bounds.Top + 1), _shot, ColorAt(px)));
    }

    private void Finish(PickResult? result)
    {
        if (_done.Task.IsCompleted) return;
        _done.TrySetResult(result);
        Close();
    }
}
