using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Dock;

/// <param name="Checked">null = plain row; true/false = checkable row showing a tick when true.</param>
public sealed record PopupRow(string Text, Action Invoke, bool? Checked = null);

/// <summary>
/// A tiny non-activating glass popup (window list / context menu). It closes itself when the pointer has left
/// it for a moment, never having entered it for a while, or after a row is chosen.
/// </summary>
public partial class DockPopup : Window
{
    private readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private bool _closed;

    public DockPopup(IReadOnlyList<PopupRow> rows)
    {
        InitializeComponent();
        Rows.ItemsSource = rows;
        SourceInitialized += (_, _) => DockWindowNative.ApplyDockStyles(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        MouseEnter += (_, _) => _autoClose.Stop();
        MouseLeave += (_, _) => { _autoClose.Interval = TimeSpan.FromMilliseconds(400); _autoClose.Start(); };
        _autoClose.Tick += (_, _) => CloseIfOpen();
    }

    /// <summary>Raised once when the popup closes, for any reason.</summary>
    public event Action? Dismissed;

    /// <summary>Shows the popup centred above <paramref name="anchorCenterX"/>, its bottom at <paramref name="bottomY"/> (physical px).</summary>
    public void ShowAbove(double anchorCenterX, double bottomY, RectI monitor, uint dpi)
    {
        var scale = dpi / 96.0;
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = DesiredSize.Width; var h = DesiredSize.Height;
        var left = Math.Clamp(anchorCenterX / scale - w / 2, monitor.Left / scale + 8, monitor.Right / scale - w - 8);
        Left = left;
        Top = bottomY / scale - h;
        Show();
        _autoClose.Interval = TimeSpan.FromMilliseconds(1500);
        _autoClose.Start();
    }

    public void CloseIfOpen()
    {
        if (_closed) return;
        _closed = true;
        _autoClose.Stop();
        Dismissed?.Invoke();
        Close();
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
