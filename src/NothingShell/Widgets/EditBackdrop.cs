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
    /// <param name="add">Called with the ＋ Add button when it is clicked (the picker opens under it).</param>
    public static FrameworkElement Build(double widthDip, double heightDip, Point originDip, double scale, Action done, Action<FrameworkElement>? add = null)
    {
        var grid = new DotGrid(originDip, DesktopLayout.Snap * scale, Math.Max(1, 1.5 * scale));
        var root = new Grid { Width = widthDip, Height = heightDip, Background = Tint, Children = { grid, Pill(done, add) } };
        return root;
    }

    private static FrameworkElement Pill(Action done, Action<FrameworkElement>? add)
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
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { text } };
        if (add is not null)
        {
            var addButton = new Border
            {
                BorderBrush = TileChrome.Text, BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 4, 14, 4), Margin = new Thickness(14, 0, 0, 0), Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Child = new TextBlock { Text = "＋ Add", FontFamily = TileChrome.Font, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TileChrome.Text },
            };
            addButton.MouseLeftButtonUp += (_, e) => { e.Handled = true; add(addButton); };
            row.Children.Add(addButton);
        }
        row.Children.Add(button);
        return new Border
        {
            // Below the notch (top centre), so the notch never covers it.
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 64, 0, 0),
            Background = WidgetCard.CardBrush, BorderBrush = WidgetCard.EdgeBrush, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(22), Padding = new Thickness(18, 7, 7, 7),
            Child = row,
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
/// spot overlaps another, the size chip at the bottom right (mockup <c>.chip</c>), a photo frame's shape chip beside
/// it, and ✕ (remove) at the top left (Plan 5b).
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
        // Alpha 1, not 0: fully transparent pixels of a per-pixel-transparent window pass clicks through (the bare clock).
        Grab = new Border { Background = TileChrome.FrozenArgb(0x01, 0x00, 0x00, 0x00), Cursor = Cursors.SizeAll };
        var root = new Grid { Children = { card, Grab, Outline } };
        var chips = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 12, 12),
        };
        if (p.Kind == WidgetKind.Photo)
        {
            ShapeChip = MakeChip(p.Config?.Shape == "circle" ? "● ▸" : "■ ▸");
            ShapeChip.Margin = new Thickness(0, 0, 6, 0);
            chips.Children.Add(ShapeChip);
        }
        if (WidgetRules.Sizes(p.Kind).Count > 1)
        {
            Chip = MakeChip($"{p.Size.W}×{p.Size.H} ▸");
            chips.Children.Add(Chip);
        }
        if (chips.Children.Count > 0) root.Children.Add(chips);
        Close = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = TileChrome.Frozen(0x2A, 0x2A, 0x2A),
            BorderBrush = WidgetCard.EdgeBrush, BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "✕", FontSize = 14, Foreground = TileChrome.Text,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Close.MouseLeftButtonDown += (_, e) => e.Handled = true; // never starts a drag
        root.Children.Add(Close);
        Root = root;
    }

    private static Border MakeChip(string text)
    {
        var chip = new Border
        {
            Background = TileChrome.Text, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 4), Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = text, FontSize = 11, Foreground = ChipText,
                FontFamily = Application.Current?.TryFindResource("Font.Mono") as FontFamily ?? new FontFamily("Consolas"),
            },
        };
        chip.MouseLeftButtonDown += (_, e) => e.Handled = true; // never starts a drag
        return chip;
    }

    public FrameworkElement Root { get; }
    public Border Grab { get; }
    public Rectangle Outline { get; }
    public Border? Chip { get; }
    public Border? ShapeChip { get; }
    public Border Close { get; }

    public void ShowOverlap(bool overlaps) => Outline.Visibility = overlaps ? Visibility.Visible : Visibility.Collapsed;
}
