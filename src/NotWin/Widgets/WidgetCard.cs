using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NotWin.Core;
using NotWin.Core.Widgets;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>A desktop widget: builds its content for a size (without the card; <see cref="WidgetCard"/> adds it).</summary>
public interface IDesktopWidget : IDisposable
{
    WidgetKind Kind { get; }

    /// <summary>The clock has no card (spec §6.5: drawn straight on the wallpaper).</summary>
    bool Bare => false;

    /// <summary>The instance's own settings (Plan 5b), given before every <see cref="Build"/>.</summary>
    void Configure(WidgetConfig? config) { }

    FrameworkElement Build(WidgetSize size);

    /// <summary>Where the widget now is on screen (physical px) and the monitor it is on (the clock samples the wallpaper).</summary>
    void Placed(RectI screen, RectI monitor) { }

    /// <summary>A full-screen app came to the front (true) or left (false): stop / restart anything that animates.</summary>
    void SetPaused(bool paused) { }

    /// <summary>The user removed this widget (before <see cref="IDisposable.Dispose"/>): delete what belongs only to it.</summary>
    void Removed() { }

    /// <summary>The card's colour (Plan 13): dark by default; a toggle turns light when on, red when blocked.</summary>
    Surface Surface => Surface.Dark;

    /// <summary>
    /// Raised when <see cref="Surface"/> changed: <see cref="DesktopWidgets"/> builds this one widget's card again.
    /// </summary>
    event Action? CardChanged { add { } remove { } }
}

/// <summary>The three card colours of the Nothing widget sheet (Plan 13).</summary>
public enum Surface { Dark, Light, Red }

/// <summary>
/// Widget card (spec §5, §6.5): black #1A1A1A, radius 26, a 1.5 px inner edge rgba(255,255,255,0.13) so cards show on
/// dark wallpapers, padding 16. The clock is bare (no card). Plan 13 adds a light card (#E8E8E8, ink content), a red
/// card (#D71921, white content, no edge) and round cards (radius half the shorter side, padding 10).
/// </summary>
public static class WidgetCard
{
    public static readonly Brush CardBrush = TileChrome.Frozen(0x1A, 0x1A, 0x1A);
    public static readonly Brush EdgeBrush = TileChrome.FrozenArgb(0x21, 0xFF, 0xFF, 0xFF);
    private static readonly Brush LightBrush = TileChrome.Frozen(0xE8, 0xE8, 0xE8), LightEdge = TileChrome.FrozenArgb(0x14, 0, 0, 0);
    private static readonly Brush InkBrush = TileChrome.Frozen(0x10, 0x10, 0x10), White = TileChrome.Frozen(0xFF, 0xFF, 0xFF);
    private static readonly Brush DimDark = TileChrome.FrozenArgb(0x66, 0xE8, 0xE8, 0xE8), DimLight = TileChrome.FrozenArgb(0x66, 0x10, 0x10, 0x10),
        DimRed = TileChrome.FrozenArgb(0x99, 0xFF, 0xFF, 0xFF);

    public static FrameworkElement Wrap(FrameworkElement content, bool bare) => Wrap(content, Surface.Dark, false, default, bare);

    public static FrameworkElement Wrap(FrameworkElement content, Surface surface, bool round, WidgetSize size, bool bare)
    {
        if (bare) return content;
        var radius = round ? Math.Min(size.PxW, size.PxH) / 2.0 : 26;
        // Plan 13 follow-up: a dark card is the theme's card (light grey in Windows' light mode).
        var (background, edge) = surface switch
        {
            Surface.Light => (LightBrush, LightEdge),
            Surface.Red => (TileChrome.Red, EdgeBrush),
            _ => (WidgetTheme.Card, WidgetTheme.Edge),
        };
        return new Border
        {
            Background = background,
            BorderBrush = edge,
            BorderThickness = new Thickness(surface == Surface.Red ? 0 : 1.5),
            CornerRadius = new CornerRadius(radius), Padding = new Thickness(round ? 10 : 16), ClipToBounds = true, Child = content,
        };
    }

    /// <summary>The content colour on a surface: text on dark, ink on light, white on red.</summary>
    public static Brush Ink(Surface surface) => surface switch { Surface.Light => InkBrush, Surface.Red => White, _ => WidgetTheme.Text };

    /// <summary>A quieter content colour (labels, unlit dots) on a surface.</summary>
    public static Brush Dim(Surface surface) => surface switch
    {
        Surface.Light => DimLight,
        Surface.Red => DimRed,
        _ => WidgetTheme.Light ? DimLight : DimDark,
    };
}
