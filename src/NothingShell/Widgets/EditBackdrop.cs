using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NothingShell.Core.Widgets;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Widgets;

/// <summary>
/// Edit mode's backdrop (spec §6.5, mockup edit mode): a window on the desktop layer under the widgets, covering the
/// primary monitor, with the 74 px snap grid as dots (3 px, rgba(110,110,110,.7), at the widget scale) and a pill near
/// the top centre — "Drag widgets · size chips change size" and Done. Content is in DIPs; the grid follows the layout
/// origin and scale it is given.
/// </summary>
public static class EditBackdrop
{
    private static readonly Brush DotBrush = TileChrome.FrozenArgb(0xB3, 0x6E, 0x6E, 0x6E);
    private static readonly Brush Tint = TileChrome.FrozenArgb(0x4D, 0x00, 0x00, 0x00);

    /// <param name="widthDip">Monitor width in DIPs.</param>
    /// <param name="heightDip">Monitor height in DIPs.</param>
    /// <param name="originDip">The layout origin in DIPs (unscaled origin × widget scale).</param>
    /// <param name="scale">Widget scale (S/M/L).</param>
    /// <param name="done">Called when Done is clicked.</param>
    public static FrameworkElement Build(double widthDip, double heightDip, Point originDip, double scale, Action done)
    {
        var grid = new DotGrid(originDip, DesktopLayout.Snap * scale, Math.Max(1, 1.5 * scale));
        var root = new Grid { Width = widthDip, Height = heightDip, Background = Tint, Children = { grid, Pill(done) } };
        return root;
    }

    private static FrameworkElement Pill(Action done)
    {
        var text = TileChrome.Body("Drag widgets · size chips change size", 13, FontWeights.Medium);
        text.VerticalAlignment = VerticalAlignment.Center;
        var doneText = new TextBlock { Text = "Done", FontFamily = TileChrome.Font, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TileChrome.Frozen(0x11, 0x11, 0x11) };
        var button = new Border
        {
            Background = TileChrome.Text, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(14, 0, 0, 0), Cursor = Cursors.Hand, Child = doneText,
        };
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; done(); };
        return new Border
        {
            // Below the notch (top centre), so the notch never covers it.
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 64, 0, 0),
            Background = WidgetCard.CardBrush, BorderBrush = WidgetCard.EdgeBrush, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(22), Padding = new Thickness(18, 7, 7, 7),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { text, button } },
        };
    }

    private sealed class DotGrid(Point origin, double pitch, double radius) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            if (pitch < 4) return;
            for (var y = origin.Y; y <= ActualHeight; y += pitch)
                for (var x = origin.X; x <= ActualWidth; x += pitch)
                    dc.DrawEllipse(DotBrush, null, new Point(x, y), radius, radius);
        }
    }
}

/// <summary>
/// What a widget shows in edit mode, on top of its card (unscaled widget units): a transparent layer that takes every
/// click (the content ignores clicks while editing, and drags start here), a red dashed outline while the widget's
/// spot overlaps another, and the size chip at the bottom right (mockup <c>.chip</c>).
/// </summary>
public sealed class EditChrome
{
    private static readonly Brush ChipText = TileChrome.Frozen(0x11, 0x11, 0x11);

    public EditChrome(FrameworkElement card, WidgetPlacement p, bool overlaps, double scale)
    {
        Outline = new Rectangle
        {
            Stroke = TileChrome.Red, StrokeThickness = 2 / scale, StrokeDashArray = [4, 3], RadiusX = 26, RadiusY = 26,
            IsHitTestVisible = false, Visibility = overlaps ? Visibility.Visible : Visibility.Collapsed,
        };
        Grab = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        var root = new Grid { Children = { card, Grab, Outline } };
        if (WidgetRules.Sizes(p.Kind).Count > 1)
        {
            Chip = new Border
            {
                Background = TileChrome.Text, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 12, 12),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = $"{p.Size.W}×{p.Size.H} ▸", FontSize = 11, Foreground = ChipText,
                    FontFamily = Application.Current?.TryFindResource("Font.Mono") as FontFamily ?? new FontFamily("Consolas"),
                },
            };
            Chip.MouseLeftButtonDown += (_, e) => e.Handled = true; // never starts a drag
            root.Children.Add(Chip);
        }
        Root = root;
    }

    public FrameworkElement Root { get; }
    public Border Grab { get; }
    public Rectangle Outline { get; }
    public Border? Chip { get; }

    public void ShowOverlap(bool overlaps) => Outline.Visibility = overlaps ? Visibility.Visible : Visibility.Collapsed;
}
