using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NotWin.Theme;

namespace NotWin.Notch.Dashboard;

/// <summary>
/// The notch dashboard's look in the current theme (Plan 13 follow-up, user 2026-11-26: "the notch tiles are still
/// black"). Same API as <see cref="TileChrome"/> (which stays dark for Settings and the edit popups), but every colour
/// follows <see cref="ShellTheme.Light"/>: light mode = #EFEFEF tiles with ink text inside the frosted white pill.
/// Tiles are built each time the dashboard shows a page, so a theme change takes effect on the next open.
/// </summary>
public static class DashChrome
{
    private static readonly Brush LightTile = Frozen(0xEF, 0xEF, 0xEF), LightHover = Frozen(0xE6, 0xE6, 0xE6);
    private static readonly Brush LightText = Frozen(0x10, 0x10, 0x10), LightDim = Frozen(0x6B, 0x6B, 0x6B);
    private static readonly Brush LightEdge = FrozenArgb(0x0F, 0x00, 0x00, 0x00), LightDark = Frozen(0xF2, 0xF2, 0xF2);
    private static readonly Brush LightOff = Frozen(0xD6, 0xD6, 0xD6), DarkOff = Frozen(0x33, 0x33, 0x33);

    private static bool Light => ShellTheme.Light;

    public static Brush TileBrush => Light ? LightTile : TileChrome.TileBrush;
    public static Brush HoverBrush => Light ? LightHover : TileChrome.HoverBrush;
    public static Brush Text => Light ? LightText : TileChrome.Text;
    public static Brush Dim => Light ? LightDim : TileChrome.Dim;
    public static Brush Edge => Light ? LightEdge : TileChrome.Edge;

    /// <summary>The colour drawn on top of <see cref="Text"/> (a play glyph on a filled circle, a toggle's knob).</summary>
    public static Brush Dark => Light ? LightDark : TileChrome.Dark;

    /// <summary>A step quieter than <see cref="Text"/>: hints, placeholders.</summary>
    public static Brush Muted => Light ? Frozen(0x55, 0x55, 0x55) : Frozen(0xCC, 0xCC, 0xCC);

    /// <summary>A small chip or highlighted row inside a tile.</summary>
    public static Brush Chip => Light ? Frozen(0xE2, 0xE2, 0xE2) : Frozen(0x22, 0x22, 0x22);

    public static Brush ChipHover => Light ? Frozen(0xDA, 0xDA, 0xDA) : Frozen(0x26, 0x26, 0x26);

    public static Brush Red => TileChrome.Red;
    public static FontFamily Font => TileChrome.Font;
    public static FontFamily Mono => TileChrome.Mono;

    public static SolidColorBrush Frozen(byte r, byte g, byte b) => TileChrome.Frozen(r, g, b);
    public static SolidColorBrush FrozenArgb(byte a, byte r, byte g, byte b) => TileChrome.FrozenArgb(a, r, g, b);

    /// <summary><see cref="TileChrome.Tile"/> in the theme's colours.</summary>
    public static Border Tile(UIElement content, bool center = false, Brush? background = null)
    {
        var tile = TileBrush;
        var hover = HoverBrush;
        var b = new Border
        {
            Background = background ?? tile, BorderBrush = Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22), Padding = new Thickness(12), ClipToBounds = true, Child = content,
        };
        if (center && content is FrameworkElement fe)
        {
            b.Padding = new Thickness(6);
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.VerticalAlignment = VerticalAlignment.Center;
        }
        if (background is null)
        {
            b.MouseEnter += (_, _) => b.Background = hover;
            b.MouseLeave += (_, _) => b.Background = tile;
        }
        return b;
    }

    public static FrameworkElement Icon(string name, double size, Brush? stroke = null) => TileChrome.Icon(name, size, stroke ?? Text);

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

    public static TextBlock Label(string text, Brush? brush = null) => TileChrome.Label(text, brush ?? Dim);

    public static TextBlock Body(string text, double size, FontWeight weight, Brush? brush = null) => TileChrome.Body(text, size, weight, brush ?? Text);

    /// <summary>A filled circle with a play / pause glyph (light circle in dark mode, ink circle in light mode).</summary>
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

    /// <summary>Toggle switch 40×24 in the theme's colours.</summary>
    public static Border Toggle(bool on)
    {
        var knob = new Ellipse
        {
            Width = 18, Height = 18, Fill = on ? Dark : Text,
            HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left, Margin = new Thickness(3),
        };
        return new Border
        {
            Width = 40, Height = 24, CornerRadius = new CornerRadius(12), Background = on ? Text : (Light ? LightOff : DarkOff),
            Child = new Grid { Children = { knob } }, Cursor = System.Windows.Input.Cursors.Hand,
        };
    }
}
