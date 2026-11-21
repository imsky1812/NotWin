using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Widgets;

/// <summary>
/// A card-style popup next to a desktop widget that can take the keyboard (desktop widgets cannot): the Notes and
/// Alarm editors (Plan 9). Closes on Esc or when it loses focus; <see cref="Closing"/> lets editors save first.
/// </summary>
public class EditorPopupWindow : Window
{
    private bool _closed;

    protected EditorPopupWindow(FrameworkElement content, double width)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = width;
        Content = new Border
        {
            Background = WidgetCard.CardBrush, BorderBrush = WidgetCard.EdgeBrush, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(20), Padding = new Thickness(14), Child = content,
        };
        Deactivated += (_, _) => CloseIfOpen();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; CloseIfOpen(); } };
        Closed += (_, _) => _closed = true;
    }

    public bool IsOpen => !_closed;

    public void CloseIfOpen()
    {
        if (_closed) return;
        _closed = true;
        Dispatcher.BeginInvoke(() => { try { Close(); } catch (InvalidOperationException) { } });
    }

    /// <summary>Shows the popup right of <paramref name="widget"/> (screen px), or left of it without room, and activates it.</summary>
    public void ShowNextTo(RectI widget)
    {
        Opacity = 0;
        Left = widget.Left;
        Top = widget.Top;
        Show();
        UpdateLayout();
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var w = (int)Math.Ceiling(ActualWidth * scale);
        var h = (int)Math.Ceiling(ActualHeight * scale);
        var gap = (int)Math.Round(10 * scale);
        var (mon, _) = DockWindowNative.GetPrimaryMonitor();
        var left = widget.Right + gap + w <= mon.Right ? widget.Right + gap : Math.Max(mon.Left, widget.Left - gap - w);
        var top = Math.Clamp(widget.Top, mon.Top, Math.Max(mon.Top, mon.Bottom - h));
        var hwnd = new WindowInteropHelper(this).Handle;
        DockWindowNative.PlaceTopmost(hwnd, new RectI(left, top, left + w, top + h));
        Opacity = 1;
        ForegroundNative.Activate(hwnd);
        Activate();
    }
}
