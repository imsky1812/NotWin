using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NotWin.Core;
using NotWin.Native;

namespace NotWin.Dock;

/// <param name="Checked">null = plain row; true/false = checkable row showing a tick when true.</param>
public sealed record PopupRow(string Text, Action Invoke, bool? Checked = null);

/// <summary>
/// A tiny non-activating glass popup (window list / context menu). It stays open until a row is chosen, a mouse
/// button goes down outside the popup and its owner dock, 400 ms after the pointer leaves it (once it has entered),
/// or 8 s if the pointer never enters it. The outside-click check polls only while the popup is open.
/// </summary>
public partial class DockPopup : Window
{
    private static readonly TimeSpan NeverEnteredTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan LeaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly DispatcherTimer _autoClose = new() { Interval = NeverEnteredTimeout };
    private readonly DispatcherTimer _outsideClick = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private nint _hwnd, _owner;
    private bool _closed;

    public DockPopup(IReadOnlyList<PopupRow> rows)
    {
        InitializeComponent();
        Rows.ItemsSource = rows;
        SourceInitialized += (_, _) => DockWindowNative.ApplyDockStyles(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        MouseEnter += (_, _) => _autoClose.Stop();;
        MouseLeave += (_, _) => { _autoClose.Interval = LeaveDelay; _autoClose.Start(); };
        _autoClose.Tick += (_, _) => CloseIfOpen();
        _outsideClick.Tick += (_, _) => CheckOutsideClick();
    }

    /// <summary>Raised once when the popup closes, for any reason.</summary>
    public event Action? Dismissed;

    /// <summary>
    /// Shows the popup centred above <paramref name="anchorCenterX"/> with its bottom at <paramref name="bottomY"/>
    /// (both physical px), clamped to <paramref name="monitor"/>. The popup is owned by the dock window (an owned
    /// window always stays above its owner) and placed with SetWindowPos in physical pixels from its real laid-out
    /// size: measuring before the window is shown under-reports the height (rows are not generated yet), which
    /// let it grow down over the pill.
    /// </summary>
    public void ShowAbove(nint ownerHwnd, double anchorCenterX, double bottomY, RectI monitor)
    {
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        helper.Owner = ownerHwnd;
        _owner = ownerHwnd;
        Opacity = 0; // invisible until placed, so it never flashes at the wrong spot
        Left = monitor.Left; Top = monitor.Top;
        Show();
        UpdateLayout();

        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        var wPx = (int)Math.Ceiling(ActualWidth * scale);
        var hPx = (int)Math.Ceiling(ActualHeight * scale);
        var margin = (int)Math.Round(8 * scale);
        var left = (int)Math.Round(anchorCenterX - wPx / 2.0);
        left = Math.Max(monitor.Left + margin, Math.Min(left, monitor.Right - wPx - margin));
        var bottom = (int)Math.Round(bottomY);
        var rect = new RectI(left, bottom - hPx, left + wPx, bottom);
        DockWindowNative.PlaceTopmost(helper.EnsureHandle(), rect);
        Opacity = 1;
        Log.Info($"popup placed: rect={rect} dpiScale={scale:F2} owner={ownerHwnd}");

        _hwnd = helper.EnsureHandle();
        _autoClose.Interval = NeverEnteredTimeout;
        _autoClose.Start();
        _outsideClick.Start();
    }

    private void CheckOutsideClick()
    {
        if (_closed || !DockWindowNative.AnyMouseButtonDown()) return;
        if (DockWindowNative.GetCursorPos() is not { } c) return;
        static bool In(RectI r, PointI p) => p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;
        var inPopup = In(DockWindowNative.GetWindowRect(_hwnd), c);
        var inDock = _owner != 0 && In(DockWindowNative.GetWindowRect(_owner), c);
        if (!inPopup && !inDock) CloseIfOpen();
    }

    public void CloseIfOpen()
    {
        if (_closed) return;
        _closed = true;
        _autoClose.Stop();
        _outsideClick.Stop();
        Dismissed?.Invoke();
        Close();
    }

    // Closed from outside (WM_CLOSE, owner destroyed): CloseIfOpen never ran, so without this the timers keep ticking
    // and Dismissed never fires, which leaves the dock's PanelOpen stuck on (dock pinned visible).
    protected override void OnClosed(EventArgs e)
    {
        _autoClose.Stop();
        _outsideClick.Stop();
        if (!_closed)
        {
            _closed = true;
            Dismissed?.Invoke();
        }
        base.OnClosed(e);
    }

    private void OnRowUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var row = (PopupRow)((FrameworkElement)sender).DataContext;
        CloseIfOpen();
        try { row.Invoke(); }
        catch (Exception ex) { Log.Error("popup row failed", ex); }
    }
}
