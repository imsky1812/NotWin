using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using NothingShell.Core;
using NothingShell.Core.Dock;
using NothingShell.Native;

namespace NothingShell.Dock;

/// <summary>
/// The dot drain's overlay (design 2026-11-20): a transparent, topmost, click-through window over the dock's monitor,
/// shared by every running animation and closed by <see cref="DotDrainController"/> when the last ends. Snapshots use a
/// tiny non-layered window at the window's spot that shows the window's live DWM picture for a couple of frames; that
/// region of the screen is then read as grey cells (never kept).
/// </summary>
public sealed class DotDrainOverlay : IDrainOverlay
{
    private const int ExTransparent = 0x20, ExToolWindow = 0x80, ExNoActivate = 0x08000000;
    private static readonly Brush White = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)));
    private static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x23, 0x1F)));
    private readonly RectI _monitor;
    private readonly double _scale; // physical px per DIP
    private readonly List<Anim> _anims = [];
    private readonly DrawingVisual _visual = new();
    private Window? _window;
    private bool _rendering;

    private sealed record Anim(IReadOnlyList<DrainDot> Dots, double Tx, double Ty, bool Restore, Action Done, Stopwatch Clock, double DotR);

    public DotDrainOverlay(RectI monitor, double dpiScale)
    {
        _monitor = monitor;
        _scale = dpiScale;
    }

    public void Snapshot(nint hwnd, RectI screen, int stepPx, Action<byte[]?> done)
    {
        var shot = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Topmost = true, Background = Brushes.Black, Width = 1, Height = 1, Left = -32000, Top = -32000,
        };
        DwmThumbnail? thumb = null;
        try
        {
            shot.Show();
            var h = new WindowInteropHelper(shot).Handle;
            DockWindowNative.AddExStyle(h, ExTransparent | ExToolWindow | ExNoActivate);
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
            Log.Error("dot drain snapshot failed", e);
            thumb?.Dispose();
            try { shot.Close(); } catch (InvalidOperationException) { }
            done(null);
            return;
        }
        // Let DWM compose the picture (two frames), then read the region and take the stand-in away.
        var wait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            byte[]? gray = null;
            try { gray = ScreenCaptureNative.CaptureGray(screen, stepPx); }
            catch (Exception e) { Log.Error("dot drain capture failed", e); }
            thumb.Dispose();
            try { shot.Close(); } catch (InvalidOperationException) { }
            done(gray);
        };
        wait.Start();
    }

    public void Play(IReadOnlyList<DrainDot> dots, double targetX, double targetY, bool restore, Action done)
    {
        EnsureWindow();
        var dotR = dots.Count > 0 ? dots.Max(d => d.R) : 6;
        _anims.Add(new Anim(dots, targetX, targetY, restore, done, Stopwatch.StartNew(), dotR));
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
        w.SourceInitialized += (_, _) => DockWindowNative.AddExStyle(new WindowInteropHelper(w).Handle, ExTransparent | ExToolWindow | ExNoActivate);
        w.Show();
        DockWindowNative.PlaceTopmost(new WindowInteropHelper(w).Handle, _monitor);
        _window = w;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var finished = new List<Anim>();
        using (var dc = _visual.RenderOpen())
        {
            foreach (var a in _anims)
            {
                var t = a.Clock.Elapsed.TotalMilliseconds;
                var end = a.Restore ? DotDrain.RestoreMs : DotDrain.TotalMs + DotDrain.BlinkMs;
                if (t >= end) { finished.Add(a); continue; }
                if (a.Restore || t < DotDrain.TotalMs)
                {
                    var rim = new Pen(new SolidColorBrush(Color.FromArgb(0x59, 0, 0, 0)), 0.8 / _scale);
                    foreach (var d in a.Dots)
                    {
                        var (x, y, s, _) = DotDrain.At(d, a.Tx, a.Ty, t, a.Restore);
                        var r = d.R * s / _scale;
                        dc.DrawEllipse(White, rim, Dip(x, y), r, r);
                    }
                }
                else
                {
                    // Landed: one red dot blinks on the icon.
                    var k = 1 - (t - DotDrain.TotalMs) / DotDrain.BlinkMs;
                    var r = a.DotR / _scale;
                    dc.PushOpacity(Math.Clamp(k, 0, 1));
                    dc.DrawEllipse(Red, null, Dip(a.Tx, a.Ty), r, r);
                    dc.Pop();
                }
            }
        }
        foreach (var a in finished)
        {
            _anims.Remove(a);
            try { a.Done(); }
            catch (Exception ex) { Log.Error("dot drain completion failed", ex); }
        }
    }

    private Point Dip(double screenX, double screenY) => new((screenX - _monitor.Left) / _scale, (screenY - _monitor.Top) / _scale);

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
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
