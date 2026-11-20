using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
    private readonly List<Anim> _finished = [];
    private RectI _area; // what the overlay window covers now (screen px): only where animations move
    private readonly RectI _monitor;
    private readonly double _scale; // physical px per DIP
    private readonly List<Anim> _anims = [];
    private readonly DrawingVisual _visual = new();
    private Window? _window;
    private bool _rendering;

    private sealed record Anim(IReadOnlyList<DrainDot> Dots, RectI Window, double Tx, double Ty, double IconW, bool Restore, Action Done, Stopwatch Clock, double DotR)
    {
        public FrameStats Stats { get; } = new();
    }

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
        // Wait until DWM has composed the picture (no dark flash), read it, hand it on — the dots start on this same
        // frame — and only then take the stand-in away, so the window turns straight into dots.
        Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            byte[]? gray = null;
            try
            {
                DwmThumbnail.Flush();
                DwmThumbnail.Flush();
                gray = ScreenCaptureNative.CaptureGray(screen, stepPx);
            }
            catch (Exception e) { Log.Error("dot drain capture failed", e); }
            try { done(gray); }
            finally
            {
                Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    thumb.Dispose();
                    try { shot.Close(); } catch (InvalidOperationException) { }
                });
            }
        });
    }

    public void Play(IReadOnlyList<DrainDot> dots, RectI window, RectI icon, bool restore, Action done)
    {
        // Smoothness (user, 2026-11-21: "choppy"): a see-through window is redrawn whole every frame, so it covers only
        // the window-to-icon area (grown when another animation joins), never the whole screen.
        var needed = DotDrain.Area(window, icon, (int)Math.Ceiling(24 * _scale));
        needed = new RectI(Math.Max(needed.Left, _monitor.Left), Math.Max(needed.Top, _monitor.Top),
            Math.Min(needed.Right, _monitor.Right), Math.Min(needed.Bottom, _monitor.Bottom));
        _area = _window is null ? needed : new RectI(Math.Min(_area.Left, needed.Left), Math.Min(_area.Top, needed.Top),
            Math.Max(_area.Right, needed.Right), Math.Max(_area.Bottom, needed.Bottom));
        EnsureWindow();
        DockWindowNative.PlaceTopmost(new WindowInteropHelper(_window!).Handle, _area);
        var dotR = dots.Count > 0 ? dots.Max(d => d.R) : 6;
        var iconW = Math.Max(icon.Right - icon.Left, 24 * _scale);
        _anims.Add(new Anim(dots, window, (icon.Left + icon.Right) / 2.0, (icon.Top + icon.Bottom) / 2.0, iconW, restore, done, Stopwatch.StartNew(), dotR));
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
        _window = w;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        try { Frame(); }
        catch (Exception ex)
        {
            // An error while drawing must never end the shell: finish every animation now.
            Log.Error("dot drain frame failed", ex);
            var all = _anims.ToList();
            _anims.Clear();
            foreach (var a in all)
            {
                try { a.Done(); } catch (Exception doneError) { Log.Error("dot drain completion failed", doneError); }
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
                var t = a.Clock.Elapsed.TotalMilliseconds;
                var duration = a.Restore ? DotDrain.GenieRestoreMs : DotDrain.GenieMs;
                var end = a.Restore ? duration : duration + DotDrain.BlinkMs;
                if (t >= end) { finished.Add(a); continue; }
                a.Stats.At(t);
                if (t < duration)
                {
                    var p = a.Restore ? 1 - t / duration : t / duration;
                    foreach (var d in a.Dots)
                    {
                        var (x, y, s, visible) = DotDrain.Genie(d, a.Window, a.Tx, a.Ty, a.IconW, p);
                        if (!visible) continue;
                        var r = d.R * s / _scale;
                        dc.DrawEllipse(White, null, Dip(x, y), r, r); // no outline: half the drawing work per dot
                    }
                }
                else
                {
                    // Landed: one red dot blinks on the icon.
                    var k = 1 - (t - DotDrain.GenieMs) / DotDrain.BlinkMs;
                    var r = a.DotR / _scale;
                    dc.PushOpacity(Math.Clamp(k, 0, 1));
                    dc.DrawEllipse(Red, null, Dip(a.Tx, a.Ty), r, r);
                    dc.Pop();
                }
            }
        }
        foreach (var a in finished.ToList())
        {
            _anims.Remove(a);
            Log.Info($"dot drain: {a.Stats.Frames} frames, worst gap {a.Stats.WorstGapMs:F0} ms, {a.Dots.Count} dots, area {_area.Right - _area.Left}x{_area.Bottom - _area.Top}");
            try { a.Done(); }
            catch (Exception ex) { Log.Error("dot drain completion failed", ex); }
        }
    }

    private Point Dip(double screenX, double screenY) => new((screenX - _area.Left) / _scale, (screenY - _area.Top) / _scale);

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
