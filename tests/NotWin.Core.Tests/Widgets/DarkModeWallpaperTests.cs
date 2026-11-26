using NotWin.Core.Widgets;

namespace NotWin.Core.Tests.Widgets;

/// <summary>
/// Plan 13 follow-up (user, 2026-11-26): the Dark mode tile switches between NotWin's dark and white dot wallpapers, so
/// the wallpaper always matches the theme.
/// </summary>
public sealed class DarkModeWallpaperTests
{
    private const string Dark = @"C:\Program Files\NotWin\Wallpapers\nothing_halftone_dark.png";
    private const string Light = @"C:\Program Files\NotWin\Wallpapers\nothing_halftone_light.png";
    private static bool All(string _) => true;

    [Fact]
    public void TurningDark_UsesTheDarkWallpaper() => Assert.Equal(Dark, DarkModeWallpaper.For(true, Dark, Light, All));

    [Fact]
    public void TurningLight_UsesTheWhiteWallpaper() => Assert.Equal(Light, DarkModeWallpaper.For(false, Dark, Light, All));

    [Fact]
    public void MissingFile_LeavesTheWallpaper()
    {
        Assert.Null(DarkModeWallpaper.For(true, Dark, Light, p => p != Dark));
        Assert.Null(DarkModeWallpaper.For(false, Dark, Light, p => p != Light));
    }
}
