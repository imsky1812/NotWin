using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NothingShell.Core;
using NothingShell.Core.Dock;
using NothingShell.Native;

namespace NothingShell.Dock;

/// <summary>
/// The minimize genie's overlay (user, 2026-11-21: the window's own picture bends into the dock icon, no dots): a
/// transparent, topmost, click-through window over only the window-to-icon area, shared by every running animation and
/// closed by <see cref="DotDrainController"/> when the last ends. The picture is cut once into horizontal strips that
/// are placed along the genie funnel each frame (<see cref="GenieStrips"/>) — a few dozen draws per frame.
/// Snapshots use a tiny non-layered stand-in window at the window's spot that shows the window's live DWM picture for
/// a frame; that region of the screen is copied (never kept beyond the animation).
/// </summary>
public sealed class DotDrainOverlay : IDrainOverlay
{
    private const int ExTransparent = 0x20, ExToolWindow = 0x80, ExNoActivate = 0x08000000;
    private const int MaxPictureWidth = 1600; // larger windows are scaled down (enough for a half-second shrink)
    private static readonly Brush Card = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));
    private readonly RectI _monitor;
    private readonly double _scale; // physical px per DIP
    private readonly List<Anim> _anims = [];
    private readonly List<Anim> _finished = [];
    private readonly DrawingVisual _visual = new();
    private RectI _area;
    private Window? _window;
    private bool _rendering;

    private sealed record Anim(Brush?[] Strips, RectI Window, double Tx, double Ty, double IconW, bool Restore, Action Done, Stopwatch Clock)
    {
        public FrameStats Stats { get; } = new();
    }

    public DotDrainOverlay(RectI monitor, double dpiScale)
    {
        _monitor = monitor;
        _scale = dpiScale;
    }

    public void Snapshot(nint hwnd, RectI screen, Action<object?> done)
    {
        var shot = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Topmost = true, Background = Brushes.Black, Width = 1, Height = 1, Left = -32000, Top = -32000,
        };
        shot.SourceInitialized += (_, _) => DockWindowNative.AddExStyle(new WindowInteropHelper(shot).Handle, ExTransparent | ExToolWindow | ExNoActivate);
        DwmThumbnail? thumb = null;
        try
        {
            shot.Show();
            var h = new WindowInteropHelper(shot).Handle;
            DockWindowNative.PlaceTopmost(h, screen);
            thumb = DwmThumbnail.Register(h, hwnd);
            if (thumb is null || !thumb.Place(new RectI(0, 0, screen.Right - screen.Left, screen.Bottom - screen.Top)))
            {
                thumb?.Dispose();
                shot.Close();
                done(null);
                return;
            }
        }
        catch (Exception e)
        {
            Log.Error("minimize genie snapshot failed", e);
            thumb?.Dispose();
            try { shot.Close(); } catch (InvalidOperationException) { }
            done(null);
            return;
        }
        // Wait until DWM has composed the picture, copy it, hand it on (the genie's first frame), and only then take the
        // stand-in away, so the window turns straight into the animation.
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            BitmapSource? picture = null;
            try
            {
                DwmThumbnail.Flush();
                DwmThumbnail.Flush();
                picture = Copy(screen);
            }
            catch (Exception e) { Log.Error("minimize genie capture failed", e); }
            try { done(picture); }
            finally
            {
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    thumb.Dispose();
                    try { shot.Close(); } catch (InvalidOperationException) { }
                });
            }
        });
    }

    // The screen region as a frozen bitmap, scaled down when very wide.
    private static BitmapSource? Copy(RectI screen)
    {
        var hbmp = ScreenCaptureNative.CaptureRegion(screen);
        if (hbmp == 0) return null;
        try
        {
            BitmapSource bmp = Imaging.CreateBitmapSourceFromHBitmap(hbmp, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            if (bmp.PixelWidth > MaxPictureWidth)
            {
                var k = (double)MaxPictureWidth / bmp.PixelWidth;
                bmp = new TransformedBitmap(bmp, new ScaleTransform(k, k));
            }
            bmp = new WriteableBitmap(bmp); // materialise once: strips crop from memory, not from a transform chain
            bmp.Freeze();
            return bmp;
        }
        finally { ScreenCaptureNative.Delete(hbmp); }
    }

    public void Play(object? picture, RectI window, RectI icon, bool restore, Action done)
    {
        // Only the window-to-icon area: a see-through window is redrawn whole every frame (user, 2026-11-21: "choppy").
        var needed = DotDrain.Area(window, icon, (int)Math.Ceiling(24 * _scale));
        needed = new RectI(Math.Max(needed.Left, _monitor.Left), Math.Max(needed.Top, _monitor.Top),
            Math.Min(needed.Right, _monitor.Right), Math.Min(needed.Bottom, _monitor.Bottom));
        _area = _window is null ? needed : new RectI(Math.Min(_area.Left, needed.Left), Math.Min(_area.Top, needed.Top),
            Math.Max(_area.Right, needed.Right), Math.Max(_area.Bottom, needed.Bottom));
        EnsureWindow();
        DockWindowNative.PlaceTopmost(new WindowInteropHelper(_window!).Handle, _area);

        // Every strip is a brush onto the SAME picture (its rows as the viewbox), so the picture goes to the graphics card
        // once — 120 separate cropped bitmaps were 120 uploads and a ~0.1 s stall on the first frame (live log 2026-11-21).
        var count = GenieStrips.CountFor(window.Bottom - window.Top);
        var strips = new Brush?[count];
        if (picture is BitmapSource bmp)
            for (var i = 0; i < count; i++)
            {
                var y0 = (double)i / count * bmp.PixelHeight;
                var y1 = (double)(i + 1) / count * bmp.PixelHeight;
                var brush = new ImageBrush(bmp)
                {
                    ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, y0 * 96 / bmp.DpiY, bmp.Width, (y1 - y0) * 96 / bmp.DpiY),
                    Stretch = Stretch.Fill,
                };
                brush.Freeze();
                strips[i] = brush;
            }
        var iconW = Math.Max(icon.Right - icon.Left, 24 * _scale);
        // The clock starts on the first drawn frame (Frame), so a slow first frame never makes the window jump ahead.
        _anims.Add(new Anim(strips, window, (icon.Left + icon.Right) / 2.0, (icon.Top + icon.Bottom) / 2.0, iconW, restore, done, new Stopwatch()));
        if (!_rendering)
        {
            CompositionTarget.Rendering += OnRendering;
            _rendering = true;
        }
    }

    public void Close()
    {
        if (_rendering) CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
        _anims.Clear();
        var w = _window;
        _window = null;
        try { w?.Close(); } catch (InvalidOperationException) { }
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;
        var host = new VisualHost(_visual);
        var w = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = false, Topmost = true, Content = host, Left = -32000, Top = -32000, Width = 1, Height = 1,
        };
        RenderOptions.SetBitmapScalingMode(host, BitmapScalingMode.Linear);
        w.SourceInitialized += (_, _) => DockWindowNative.AddExStyle(new WindowInteropHelper(w).Handle, ExTransparent | ExToolWindow | ExNoActivate);
        w.Show();
        _window = w;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        try { Frame(); }
        catch (Exception ex)
        {
            // An error while drawing must never end the shell: finish every animation now.
            Log.Error("minimize genie frame failed", ex);
            var all = _anims.ToList();
            _anims.Clear();
            foreach (var a in all)
            {
                try { a.Done(); } catch (Exception doneError) { Log.Error("minimize genie completion failed", doneError); }
            }
        }
    }

    private void Frame()
    {
        var finished = _finished;
        finished.Clear();
        using (var dc = _visual.RenderOpen())
        {
            foreach (var a in _anims)
            {
                if (!a.Clock.IsRunning) a.Clock.Start();
                var t = a.Clock.Elapsed.TotalMilliseconds;
                var duration = a.Restore ? DotDrain.GenieRestoreMs : DotDrain.GenieMs;
                if (t >= duration) { finished.Add(a); continue; }
                a.Stats.At(t);
                var p = a.Restore ? 1 - t / duration : t / duration;
                var rects = GenieStrips.Layout(a.Window, a.Tx, a.Ty, a.IconW, p, a.Strips.Length);
                for (var i = 0; i < rects.Count; i++)
                {
                    if (rects[i] is not { } r || r.Width < 0.5 || r.Height < 0.1) continue;
                    // A hair of overlap hides seams between strips.
                    var dest = new Rect((r.X - _area.Left) / _scale, (r.Y - _area.Top) / _scale, r.Width / _scale, r.Height / _scale + 0.6);
                    dc.DrawRectangle(a.Strips[i] ?? Card, null, dest);
                }
            }
        }
        foreach (var a in finished.ToList())
        {
            _anims.Remove(a);
            Log.Info($"minimize genie: {a.Stats.Frames} frames, worst gap {a.Stats.WorstGapMs:F0} ms, {a.Strips.Length} strips, area {_area.Right - _area.Left}x{_area.Bottom - _area.Top}");
            try { a.Done(); }
            catch (Exception ex) { Log.Error("minimize genie completion failed", ex); }
        }
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private sealed class VisualHost : FrameworkElement
    {
        private readonly Visual _child;

        public VisualHost(Visual child)
        {
            _child = child;
            AddVisualChild(child);
            IsHitTestVisible = false;
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _child;
    }
}
