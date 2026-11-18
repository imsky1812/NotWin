using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Dock;

/// <summary>Where the dock sits on the primary monitor, in physical pixels.</summary>
/// <param name="Monitor">Primary monitor bounds (the edge trigger is its last pixel row).</param>
/// <param name="Pill">The visible pill.</param>
/// <param name="Window">The dock window: the pill plus the gap down to the screen bottom.</param>
/// <param name="Dpi">Primary monitor effective DPI.</param>
public readonly record struct DockPlacement(RectI Monitor, RectI Pill, RectI Window, uint Dpi);

/// <summary>
/// The floating glass pill. Borderless, per-pixel transparent, topmost, never activated, no taskbar button and
/// not in Alt+Tab. Shows/hides by sliding 90 DIP and fading over 240 ms with Ease.Standard; the HWND is hidden
/// when the dock is hidden, so an idle hidden dock costs nothing.
/// No DWM backdrop: on a per-pixel-alpha (layered) window the accent blur covers the whole window rectangle
/// (it ignores both the alpha and a window region), and DWMWA_SYSTEMBACKDROP_TYPE needs a non-layered window,
/// which would lose the anti-aliased pill and the slide. The pill is the plain Brush.Glass (#E6101010) instead.
/// </summary>
public partial class DockWindow : Window
{
    public const double PillHeight = 60;
    public const double BottomGap = 12;
    public const double SlideOffset = 90;
    public static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(240);

    private readonly DockViewModel _vm;
    private nint _hwnd;
    private DockPlacement _placement;
    private int _animation;
    private bool _repositionQueued;

    public DockWindow(DockViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnViewModelChanged;
        Pill.MouseEnter += (_, _) => HoverChanged?.Invoke(true);
        Pill.MouseLeave += (_, _) => HoverChanged?.Invoke(false);
        SourceInitialized += OnSourceInitialized;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    /// <summary>Temporary fixed width (DIP) until the items (Task 12) and tray (Task 14) size the dock.</summary>
    public double ContentWidth { get; } = 360;

    public DockPlacement Placement => _placement;

    public nint Handle => _hwnd;

    /// <summary>Mouse entered (true) or left (false) the pill.</summary>
    public event Action<bool>? HoverChanged;

    /// <summary>Raised after the dock moved because the display layout or DPI changed.</summary>
    public event Action? PlacementChanged;

    /// <summary>Creates the HWND and places it on the primary monitor without showing it.</summary>
    public void Initialize()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        Reposition();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        DockWindowNative.ApplyDockStyles(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Let WPF rescale on DPI changes, then put the dock back where it belongs.
        if (msg is DockWindowNative.WmDpiChanged or DockWindowNative.WmDisplayChange) QueueReposition();
        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(QueueReposition);

    private void QueueReposition()
    {
        if (_repositionQueued) return;
        _repositionQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _repositionQueued = false;
            Reposition();
            PlacementChanged?.Invoke();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Reposition()
    {
        if (_hwnd == 0) return;
        try
        {
            var (monitor, dpi) = DockWindowNative.GetPrimaryMonitor();
            var scale = dpi / 96.0;
            var pill = EdgeGeometry.DockBounds(monitor, ContentWidth * scale, PillHeight * scale, BottomGap * scale);
            var window = new RectI(pill.Left, pill.Top, pill.Right, monitor.Bottom);
            var changed = window != _placement.Window || dpi != _placement.Dpi;
            _placement = new DockPlacement(monitor, pill, window, dpi);
            DockWindowNative.PlaceTopmost(_hwnd, window);
            if (changed) Log.Info($"dock placed: monitor={monitor} dpi={dpi} pill={pill} window={window}");
        }
        catch (Exception e)
        {
            Log.Error("dock reposition failed", e);
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DockViewModel.IsVisible)) Animate(_vm.IsVisible);
    }

    private void Animate(bool show)
    {
        var generation = ++_animation;
        if (show)
        {
            if (!IsVisible) Show(); // ShowActivated=false -> SW_SHOWNA
            DockWindowNative.PlaceTopmost(_hwnd, _placement.Window); // re-assert position + topmost band
        }

        var ease = (KeySpline)FindResource("Ease.Standard");
        var slide = Frames(show ? 0 : SlideOffset, ease);
        slide.Completed += (_, _) => OnAnimationDone(generation, show);
        Slide.BeginAnimation(TranslateTransform.YProperty, slide);
        Pill.BeginAnimation(OpacityProperty, Frames(show ? 1 : 0, ease));
    }

    // One key frame at the end: the start is whatever the pill shows now, so an interrupted
    // show/hide reverses smoothly from its current position (default SnapshotAndReplace handoff).
    private static DoubleAnimationUsingKeyFrames Frames(double to, KeySpline ease)
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = AnimationDuration };
        a.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(AnimationDuration), ease));
        return a;
    }

    private void OnAnimationDone(int generation, bool show)
    {
        if (generation != _animation) return; // superseded by a newer show/hide

        // Pin the final values and drop the clocks so nothing keeps the render loop alive.
        Slide.Y = show ? 0 : SlideOffset;
        Slide.BeginAnimation(TranslateTransform.YProperty, null);
        Pill.Opacity = show ? 1 : 0;
        Pill.BeginAnimation(OpacityProperty, null);

        if (!show) Hide();
    }
}
