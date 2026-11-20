using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using NothingShell.Core;
using NothingShell.Core.Dock;
using NothingShell.Native;

namespace NothingShell.Dock;

/// <summary>
/// The minimize animation's overlay (user, 2026-11-21, option A): the window's live DWM picture shrinks into its dock
/// icon (<see cref="ShrinkMotion"/>). Windows draws and scales the picture on the graphics card; each frame only moves
/// it and sets its opacity, so it stays smooth. The overlay is a plain (non-layered) window over only the
/// window-to-icon area with its frame extended over the whole client area — see-through wherever it draws nothing —
/// click-through, never activated, shared by every running animation and closed when the last ends.
/// </summary>
public sealed class DotDrainOverlay : IDrainOverlay
{
    private const int ExTransparent = 0x20, ExToolWindow = 0x80, ExNoActivate = 0x08000000;
    private readonly RectI _monitor;
    private readonly double _scale; // physical px per DIP
    private readonly List<Anim> _anims = [];
    private readonly List<Anim> _finished = [];
    private RectI _area;
    private Window? _window;
    private nint _hwnd;
    private bool _rendering;

    private sealed record Anim(DwmThumbnail? Thumb, RectI Window, RectI Icon, Action Done, Stopwatch Clock)
    {
        public FrameStats Stats { get; } = new();
    }

    public DotDrainOverlay(RectI monitor, double dpiScale)
    {
        _monitor = monitor;
        _scale = dpiScale;
    }

    // No copy is taken: the animation shows Windows' own live picture of the window (minimized windows included).
    public void Snapshot(nint hwnd, RectI screen, Action<object?> done) => done(hwnd);

    public void Play(object? picture, RectI window, RectI icon, bool restore, Action done)
    {
        if (restore || picture is not nint source || source == 0) { done(); return; }
        var needed = DotDrain.Area(window, icon, (int)Math.Ceiling(24 * _scale));
        needed = new RectI(Math.Max(needed.Left, _monitor.Left), Math.Max(needed.Top, _monitor.Top),
            Math.Min(needed.Right, _monitor.Right), Math.Min(needed.Bottom, _monitor.Bottom));
        _area = _window is null ? needed : new RectI(Math.Min(_area.Left, needed.Left), Math.Min(_area.Top, needed.Top),
            Math.Max(_area.Right, needed.Right), Math.Max(_area.Bottom, needed.Bottom));
        EnsureWindow();
        DockWindowNative.PlaceTopmost(_hwnd, _area);
        var thumb = DwmThumbnail.Register(_hwnd, source);
        var anim = new Anim(thumb, window, icon, done, new Stopwatch());
        _anims.Add(anim);
        Place(anim, 0); // the first frame right away: the picture appears exactly where the window was
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
        foreach (var a in _anims) a.Thumb?.Dispose();
        _anims.Clear();
        var w = _window;
        _window = null;
        _hwnd = 0;
        try { w?.Close(); } catch (InvalidOperationException) { }
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;
        var w = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Topmost = true, Background = Brushes.Transparent, Left = -32000, Top = -32000, Width = 1, Height = 1,
        };
        w.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(w).Handle;
            DockWindowNative.AddExStyle(h, ExTransparent | ExToolWindow | ExNoActivate);
            if (HwndSource.FromHwnd(h) is { } source) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            DwmThumbnail.ExtendFrame(h); // see-through wherever nothing is drawn (checked live, 2026-11-21)
        };
        w.Show();
        _window = w;
        _hwnd = new WindowInteropHelper(w).Handle;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        try { Frame(); }
        catch (Exception ex)
        {
            // An error must never end the shell: finish every animation now.
            Log.Error("minimize animation frame failed", ex);
            var all = _anims.ToList();
            _anims.Clear();
            foreach (var a in all)
            {
                a.Thumb?.Dispose();
                try { a.Done(); } catch (Exception doneError) { Log.Error("minimize animation completion failed", doneError); }
            }
        }
    }

    private void Frame()
    {
        _finished.Clear();
        foreach (var a in _anims)
        {
            if (!a.Clock.IsRunning) a.Clock.Start();
            var t = a.Clock.Elapsed.TotalMilliseconds;
            if (t >= ShrinkMotion.MinimizeMs) { _finished.Add(a); continue; }
            a.Stats.At(t);
            Place(a, t / ShrinkMotion.MinimizeMs);
        }
        foreach (var a in _finished)
        {
            _anims.Remove(a);
            a.Thumb?.Dispose();
            Log.Info($"minimize animation: {a.Stats.Frames} frames, worst gap {a.Stats.WorstGapMs:F0} ms, area {_area.Right - _area.Left}x{_area.Bottom - _area.Top}");
            try { a.Done(); }
            catch (Exception ex) { Log.Error("minimize animation completion failed", ex); }
        }
    }

    // The picture's rectangle at progress p, in the overlay's client pixels.
    private void Place(Anim a, double p)
    {
        if (a.Thumb is null) return;
        var f = ShrinkMotion.At(a.Window, a.Icon, p);
        a.Thumb.Place(new RectI(
            (int)Math.Round(f.X - _area.Left), (int)Math.Round(f.Y - _area.Top),
            (int)Math.Round(f.X + f.W - _area.Left), (int)Math.Round(f.Y + f.H - _area.Top)),
            (byte)Math.Round(255 * f.Opacity));
    }
}
