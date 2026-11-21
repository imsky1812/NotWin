namespace NotWin.Core.Widgets;

/// <summary>
/// The clock widget has no card (spec §6.5): dark dots on light wallpapers, light dots on dark ones. Given the mean
/// relative luminance (0..1) of the wallpaper under the clock, true means draw dark dots.
/// </summary>
public static class ClockInk
{
    public static bool For(double luminance) => luminance >= 0.5;
}
