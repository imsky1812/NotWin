using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NothingShell.Notch.Dashboard;

/// <summary>
/// Shared look of the notch dashboard tiles (mockup <c>.t</c>, <c>.hd</c>, <c>.lbl2</c>, <c>.play</c>, <c>.tg</c>):
/// tile #161616 (hover #202020), radius 22, padding 12, inner 1 px #0FFFFFFF edge; Geist SemiBold 13 headers;
/// Geist Mono 9 dim uppercase labels.
/// </summary>
public static class TileChrome
{
    public static readonly Brush TileBrush = Frozen(0x16, 0x16, 0x16), HoverBrush = Frozen(0x20, 0x20, 0x20);
    public static readonly Brush Text = Frozen(0xF2, 0xF2, 0xF2), Dim = Frozen(0x8A, 0x8A, 0x8A), Red = Frozen(0xE8, 0x23, 0x1F);
    public static readonly Brush Edge = FrozenArgb(0x0F, 0xFF, 0xFF, 0xFF), Dark = Frozen(0x11, 0x11, 0x11);

    private static ResourceDictionary? s_icons;

    private static ResourceDictionary Icons => s_icons ??= new ResourceDictionary
    {
        Source = new Uri("pack://application:,,,/NothingShell;component/Notch/Dashboard/NotchIcons.xaml"),
    };

    // Fallbacks keep the chrome usable without the app's resources (tests, a failed theme load).
    public static FontFamily Font => Application.Current?.TryFindResource("Font.Geist") as FontFamily ?? new FontFamily("Segoe UI");
    public static FontFamily Mono => Application.Current?.TryFindResource("Font.GeistMono") as FontFamily ?? new FontFamily("Consolas");

    /// <summary>A tile surface. <paramref name="center"/> centres its content (mockup <c>.t.center</c>).</summary>
    public static Border Tile(UIElement content, bool center = false, Brush? background = null)
    {
        var b = new Border
        {
            Background = background ?? TileBrush,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(12),
            ClipToBounds = true,
            Child = content,
        };
        if (center && content is FrameworkElement fe)
        {
            b.Padding = new Thickness(6); // 1x1 stacks (icon, dots, label) need the room in an 84 DIP cell
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.VerticalAlignment = VerticalAlignment.Center;
        }
        if (background is null)
        {
            b.MouseEnter += (_, _) => b.Background = HoverBrush;
            b.MouseLeave += (_, _) => b.Background = TileBrush;
        }
        return b;
    }

    /// <summary>A 24-unit line icon from <c>NotchIcons.xaml</c> drawn at <paramref name="size"/> DIP.</summary>
    public static FrameworkElement Icon(string name, double size, Brush? stroke = null)
    {
        var path = new Path
        {
            Data = (Geometry)Icons["Icon." + name],
            Stroke = stroke ?? Text,
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var canvas = new Canvas { Width = 24, Height = 24, Children = { path } };
        return new Viewbox { Width = size, Height = size, Child = canvas, SnapsToDevicePixels = true };
    }

    /// <summary>A line icon as an image (white strokes on transparent), e.g. for the notch's own notification cards.</summary>
    public static ImageSource IconImage(string name)
    {
        var pen = new Pen(Text, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        group.Children.Add(new GeometryDrawing(null, pen, (Geometry)Icons["Icon." + name]));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>Header row: icon 16, title (Geist SemiBold 13), optional right-aligned element (mockup <c>.hd</c>).</summary>
    public static DockPanel Header(string icon, string title, UIElement? right = null)
    {
        var row = new DockPanel { LastChildFill = true };
        if (right is not null)
        {
            DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
            row.Children.Add(right);
        }
        var ic = Icon(icon, 16);
        ic.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(ic, System.Windows.Controls.Dock.Left);
        row.Children.Add(ic);
        row.Children.Add(new TextBlock
        {
            Text = title, FontFamily = Font, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = Text,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return row;
    }

    /// <summary>Small uppercase label (Geist Mono 9, dim, letter-spaced; mockup <c>.lbl2</c>).</summary>
    public static TextBlock Label(string text, Brush? brush = null) => new()
    {
        Text = text.ToUpperInvariant(),
        FontFamily = Mono,
        FontWeight = FontWeights.Medium,
        FontSize = 9,
        Foreground = brush ?? Dim,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Body(string text, double size, FontWeight weight, Brush? brush = null) => new()
    {
        Text = text,
        FontFamily = Font,
        FontSize = size,
        FontWeight = weight,
        Foreground = brush ?? Text,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>White circle with a dark play or pause glyph (mockup <c>.play</c>).</summary>
    public static Border PlayButton(bool playing, double size)
    {
        var glyph = playing
            ? Geometry.Parse("M 0 0 H 3.2 V 12 H 0 Z M 6.8 0 H 10 V 12 H 6.8 Z")
            : Geometry.Parse("M 1 0 L 11 6 L 1 12 Z");
        var path = new Path { Data = glyph, Fill = Dark, Width = 12, Height = 12, Stretch = Stretch.Uniform };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Text,
            Child = new Viewbox { Width = size * 0.4, Height = size * 0.4, Child = path },
            Cursor = System.Windows.Input.Cursors.Hand,
        };
    }

    /// <summary>Toggle switch 40×24 (mockup <c>.tg</c>): off #333 with a light knob; on #F2F2F2 with a dark knob.</summary>
    public static Border Toggle(bool on)
    {
        var knob = new Ellipse
        {
            Width = 18, Height = 18, Fill = on ? Dark : Text,
            HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Margin = new Thickness(3),
        };
        return new Border
        {
            Width = 40, Height = 24, CornerRadius = new CornerRadius(12),
            Background = on ? Text : Frozen(0x33, 0x33, 0x33),
            Child = new Grid { Children = { knob } },
            Cursor = System.Windows.Input.Cursors.Hand,
        };
    }

    public static SolidColorBrush Frozen(byte r, byte g, byte b) => FrozenArgb(0xFF, r, g, b);

    public static SolidColorBrush FrozenArgb(byte a, byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        br.Freeze();
        return br;
    }
}
