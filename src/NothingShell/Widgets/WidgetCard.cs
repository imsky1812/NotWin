using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NothingShell.Core;
using NothingShell.Core.Widgets;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Widgets;

/// <summary>A desktop widget: builds its content for a size (without the card; <see cref="WidgetCard"/> adds it).</summary>
public interface IDesktopWidget : IDisposable
{
    WidgetKind Kind { get; }

    /// <summary>The clock has no card (spec §6.5: drawn straight on the wallpaper).</summary>
    bool Bare => false;

    FrameworkElement Build(WidgetSize size);

    /// <summary>Where the widget now is on screen (physical px) and the monitor it is on (the clock samples the wallpaper).</summary>
    void Placed(RectI screen, RectI monitor) { }
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
