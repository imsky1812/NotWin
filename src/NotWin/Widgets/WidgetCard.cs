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
}

/// <summary>
/// Widget card (spec §5, §6.5): black #1A1A1A, radius 26, a 1.5 px inner edge rgba(255,255,255,0.13) so cards show on
/// dark wallpapers, padding 16. The clock is bare (no card).
/// </summary>
public static class WidgetCard
{
    public static readonly Brush CardBrush = TileChrome.Frozen(0x1A, 0x1A, 0x1A);
    public static readonly Brush EdgeBrush = TileChrome.FrozenArgb(0x21, 0xFF, 0xFF, 0xFF);

    public static FrameworkElement Wrap(FrameworkElement content, bool bare)
    {
        if (bare) return content;
        return new Border
        {
            Background = CardBrush, BorderBrush = EdgeBrush, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(26), Padding = new Thickness(16), ClipToBounds = true, Child = content,
        };
    }
}
