namespace NotWin.Core.Widgets;

/// <summary>
/// The Dark mode tile's wallpaper (Plan 13 follow-up). Turning dark applies NotWin's dark dot wallpaper and remembers
/// the one it replaced; turning light puts that one back, but only while the dark one is still showing (a wallpaper the
/// user picked meanwhile stays) and only if its file still exists. Called only from a click on the tile.
/// </summary>
public static class DarkModeWallpaper
{
    /// <returns>The wallpaper to apply (null = leave it) and what to remember from now on.</returns>
    public static (string? Apply, string? Remember) OnFlip(bool turningDark, string? current, string? remembered, string darkPath,
        Func<string, bool> exists)
    {
        var showingDark = string.Equals(current, darkPath, StringComparison.OrdinalIgnoreCase);
        if (turningDark)
        {
            if (!exists(darkPath)) return (null, remembered);
            return (darkPath, showingDark ? remembered : current);
        }
        var restore = showingDark && remembered is not null && exists(remembered) ? remembered : null;
        return (restore, null);
    }
}
