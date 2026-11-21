using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NothingShell.Core;
using NothingShell.Core.Widgets;
using NothingShell.Native;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Widgets;

/// <summary>
/// Edit mode's ＋ Add picker (Plan 5b): a black popup under the pill, one row per widget kind that can be added
/// (a small dot icon and the name). Closes on a pick, a click outside or Esc.
/// </summary>
public sealed class AddPicker : Window
{
    private bool _closing;

    private AddPicker(IReadOnlyList<WidgetKind> kinds, Action<WidgetKind> pick)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        var list = new StackPanel();
        foreach (var kind in kinds)
        {
            var k = kind;
            var row = new Border
            {
                Padding = new Thickness(12, 8, 18, 8), CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new DotArtView { Rows = DotArt.Rows(KindIcon(kind)), Dot = 1.6, Gap = 0.6, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = KindName(kind), FontFamily = TileChrome.Font, FontSize = 14, Foreground = TileChrome.Text, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            row.MouseEnter += (_, _) => row.Background = TileChrome.Frozen(0x2A, 0x2A, 0x2A);
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                SafeClose();
                try { pick(k); }
                catch (Exception ex) { Log.Error("adding a widget failed", ex); }
            };
            list.Children.Add(row);
        }
        Content = new Border
        {
            Background = WidgetCard.CardBrush, BorderBrush = WidgetCard.EdgeBrush, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(18), Padding = new Thickness(6), Child = list,
        };
        Deactivated += (_, _) => SafeClose();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SafeClose(); };
    }

    /// <summary>Shows the picker centred under <paramref name="below"/> (physical px).</summary>
    public static AddPicker Show(IReadOnlyList<WidgetKind> kinds, RectI below, Action<WidgetKind> pick)
    {
        var picker = new AddPicker(kinds, pick) { Opacity = 0, Left = below.Left, Top = below.Bottom };
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

    public static string KindName(WidgetKind kind) => kind switch
    {
        WidgetKind.Photo => "Photo frame",
        WidgetKind.Folder => "App folder",
        WidgetKind.Agenda => "Up next",
        WidgetKind.TicTacToe => "Tic-tac-toe",
        WidgetKind.Notes => "Notes",
        _ => kind.ToString(),
    };

    // A small dot icon per kind: the folder and camera drawings; the others share the star until they get their own.
    private static string KindIcon(WidgetKind kind) => kind switch
    {
        WidgetKind.Photo => "camera",
        WidgetKind.Folder => "folder",
        WidgetKind.Snake or WidgetKind.TicTacToe => "gamepad",
        WidgetKind.Media => "note",
        WidgetKind.Notes => "book",
        WidgetKind.Weather => "plant",
        WidgetKind.Clock or WidgetKind.Timer => "cup",
        _ => "star",
    };
}

/// <summary>Draws <see cref="DotArt"/> rows as round dots (lit = <see cref="Fill"/>, unlit not drawn).</summary>
public sealed class DotArtView : FrameworkElement
{
    public string[] Rows { get; set; } = [];
    public double Dot { get; set; } = 6;
    public double Gap { get; set; } = 2;
    public Brush Fill { get; set; } = TileChrome.Text;

    protected override Size MeasureOverride(Size availableSize)
    {
        var n = Rows.Length == 0 ? 0 : Rows.Max(r => r.Length);
        return new Size(n * (Dot + Gap), Rows.Length * (Dot + Gap));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var pitch = Dot + Gap;
        for (var y = 0; y < Rows.Length; y++)
            for (var x = 0; x < Rows[y].Length; x++)
                if (Rows[y][x] == '#')
                    dc.DrawEllipse(Fill, null, new Point(x * pitch + pitch / 2, y * pitch + pitch / 2), Dot / 2, Dot / 2);
    }
}
