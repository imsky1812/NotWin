namespace NotWin.Core.Widgets;

/// <summary>
/// The Dark mode tile's wallpaper (Plan 13 follow-up, user 2026-11-26): NotWin's dark dot wallpaper in dark mode and its
/// white one in light mode, so the desktop always matches the theme. Called only from a click on the tile.
/// </summary>
public static class DarkModeWallpaper
{
    /// <returns>The wallpaper for the new theme, or null (leave it) when that file is missing.</returns>
    public static string? For(bool turningDark, string darkPath, string lightPath, Func<string, bool> exists)
    {
        var path = turningDark ? darkPath : lightPath;
        return exists(path) ? path : null;
    }
}
