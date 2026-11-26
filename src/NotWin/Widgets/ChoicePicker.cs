using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NotWin.Core;
using NotWin.Core.Widgets;
using NotWin.Native;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// Edit mode's choice list (Plan 13): what a toggle, world clock or analogue clock shows. The same black popup as
/// <see cref="AddPicker"/>, the current choice marked with ●. Closes on a pick, a click outside or Esc.
/// </summary>
public sealed class ChoicePicker : Window
{
    private static readonly Dictionary<string, string> ToggleNames = new()
    {
        ["wifi"] = "Wi-Fi", ["bluetooth"] = "Bluetooth", ["dark"] = "Dark mode", ["awake"] = "Keep awake", ["mic"] = "Mic access",
        ["camera"] = "Camera access",
    };

    private bool _closing;

    private ChoicePicker(IReadOnlyList<(string Id, string Label)> options, string? current, Action<string> pick)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        var list = new StackPanel();
        foreach (var (id, label) in options)
        {
            var chosen = id;
            var row = new Border
            {
                Padding = new Thickness(12, 7, 18, 7), CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = id == current ? "●" : "", Width = 18, FontSize = 10, Foreground = TileChrome.Red,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        new TextBlock { Text = label, FontFamily = TileChrome.Font, FontSize = 14, Foreground = TileChrome.Text, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            row.MouseEnter += (_, _) => row.Background = TileChrome.Frozen(0x2A, 0x2A, 0x2A);
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                SafeClose();
                try { pick(chosen); }
                catch (Exception ex) { Log.Error("choosing a widget option failed", ex); }
            };
            list.Children.Add(row);
        }
        Content = new Border
        {
            Background = WidgetCard.CardBrush, BorderBrush = WidgetCard.EdgeBrush, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(18), Padding = new Thickness(6),
            // 26 time zones are taller than a small screen: the list scrolls.
            Child = new ScrollViewer { Content = list, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
        Deactivated += (_, _) => SafeClose();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SafeClose(); };
    }

    /// <summary>What a kind can choose, labelled: toggle names, the two faces, or the zones this PC has (Windows' names).</summary>
    public static IReadOnlyList<(string Id, string Label)> Labels(WidgetKind kind) => kind switch
    {
        WidgetKind.Toggle => WidgetChoices.Toggles.Select(t => (t, ToggleNames[t])).ToList(),
        WidgetKind.AnalogClock => [("light", "Light face"), ("dark", "Dark face")],
        WidgetKind.WorldClock => WidgetChoices.Zones.Select(Zone).OfType<(string, string)>().ToList(),
        _ => [],
    };

    /// <summary>The label a toggle id shows ("Wi-Fi").</summary>
    public static string ToggleName(string id) => ToggleNames.GetValueOrDefault(id, id);

    private static (string, string)? Zone(string id)
    {
        try { return (id, TimeZoneInfo.FindSystemTimeZoneById(id).DisplayName); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    /// <summary>Shows the list centred under <paramref name="below"/> (physical px).</summary>
    public static ChoicePicker Show(IReadOnlyList<(string Id, string Label)> options, string? current, RectI below, Action<string> pick)
    {
        var picker = new ChoicePicker(options, current, pick) { Opacity = 0, Left = below.Left, Top = below.Bottom };
        picker.Show();
        picker.UpdateLayout();
        var scale = VisualTreeHelper.GetDpi(picker).DpiScaleX;
        var w = (int)Math.Ceiling(picker.ActualWidth * scale);
        var h = (int)Math.Ceiling(picker.ActualHeight * scale);
        var left = (below.Left + below.Right) / 2 - w / 2;
        var top = below.Bottom + (int)Math.Round(8 * scale);
        DockWindowNative.PlaceTopmost(new WindowInteropHelper(picker).Handle, new RectI(left, top, left + w, top + h));
        picker.Opacity = 1;
        picker.Activate();
        return picker;
    }

    private void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        Dispatcher.BeginInvoke(() => { try { Close(); } catch (InvalidOperationException) { } });
    }
}
