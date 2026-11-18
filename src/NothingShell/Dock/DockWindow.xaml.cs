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
    private int? _reservedBottom;

    public DockWindow(DockViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnViewModelChanged;
        vm.Items.CollectionChanged += (_, _) => OnItemsChanged();
        Pill.Width = ContentWidth;
        Pill.MouseEnter += (_, _) => HoverChanged?.Invoke(true);
        Pill.MouseLeave += (_, _) => HoverChanged?.Invoke(false);
        SourceInitialized += OnSourceInitialized;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    // Item slot 44 + 2x2 margin, pill padding 2x10 and border 2x1 (mockup: 44 px items, 4 px gap, 10 px padding).
    private const double SlotWidth = 48;
    private const double PillChrome = 22;
    // Divider (1 + 2x6 margin) and the tray (2x10 padding + four 16 px glyphs + four 10 px gaps + 38 px time).
    private const double DividerWidth = 13;
    private const double TrayWidth = 162;
    // Start + search slots (2 x 48); their divider is one more DividerWidth.
    private const double StartSearchWidth = 2 * SlotWidth;

    /// <summary>Pill width (DIP): the item slots (at least one wide), the divider and the fixed-width tray.</summary>
    public double ContentWidth => PillChrome + StartSearchWidth + DividerWidth + SlotWidth * Math.Max(_vm.Items.Count, 1) + DividerWidth + TrayWidth;

    /// <summary>A dock item was left-clicked / right-clicked; the element anchors the popup.</summary>
    public event Action<DockItemViewModel, FrameworkElement>? ItemClicked;
    public event Action<DockItemViewModel, FrameworkElement>? ItemContext;

    /// <summary>The tray (not the overflow chevron) was clicked / the overflow chevron was clicked.</summary>
    public event Action? TrayClicked;
    public event Action? OverflowClicked;

    /// <summary>The start button was right-clicked; the element anchors the menu.</summary>
    public event Action<FrameworkElement>? StartContext;

    /// <summary>The start button was left-clicked (opens the launcher).</summary>
    public event Action? StartClicked;

    /// <summary>The search button was left-clicked (opens the launcher).</summary>
    public event Action? SearchClicked;

    private void OnStartLeftUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => StartClicked?.Invoke();

    private void OnSearchLeftUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => SearchClicked?.Invoke();

    private void OnStartRightUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el) StartContext?.Invoke(el);
    }

    private void OnTrayUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => TrayClicked?.Invoke();

    private void OnOverflowUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true; // the chevron is part of the tray: do not also open quick settings
        OverflowClicked?.Invoke();
    }

    private void OnItemLeftUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DockItemViewModel item } el) ItemClicked?.Invoke(item, el);
    }

    private void OnItemRightUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DockItemViewModel item } el) ItemContext?.Invoke(item, el);
    }

    private void OnItemsChanged()
    {
        Pill.Width = ContentWidth;
        var before = _placement.Window;
        Reposition();
        if (_placement.Window != before) PlacementChanged?.Invoke();
    }

    /// <summary>
    /// When the dock reserves space as an appbar, the bottom of the granted strip (physical px); the dock is laid out
    /// above it (pill bottom = granted bottom - gap) instead of above the raw monitor bottom. Null = not reserved.
    /// </summary>
    public void SetReservedBottom(int? bottom)
    {
        if (_reservedBottom == bottom) return;
        _reservedBottom = bottom;
        var before = _placement.Window;
        Reposition();
        if (_placement.Window != before) PlacementChanged?.Invoke();
    }

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
            var layout = _reservedBottom is { } rb ? monitor with { Bottom = Math.Min(rb, monitor.Bottom) } : monitor;
            var pill = EdgeGeometry.DockBounds(layout, ContentWidth * scale, PillHeight * scale, BottomGap * scale);
            var window = new RectI(pill.Left, pill.Top, pill.Right, layout.Bottom);
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
