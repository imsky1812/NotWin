using NotWin.Core.Sync;
using NotWin.Core.Widgets;

namespace NotWin.Core.Tests.Widgets;

/// <summary>Plan 13 follow-up: the Dark mode tile switches to NotWin's dark dot wallpaper and back.</summary>
public sealed class DarkModeWallpaperTests
{
    private const string Dark = @"C:\Program Files\NotWin\Wallpapers\nothing_halftone_dark.png", Mine = @"C:\Users\me\Pictures\beach.jpg";
    private static bool All(string _) => true;

    [Fact]
    public void TurningDark_AppliesDark_RemembersCurrent() =>
        Assert.Equal((Dark, Mine), DarkModeWallpaper.OnFlip(true, Mine, null, Dark, All));

    [Fact]
    public void TurningDark_DarkFileMissing_ChangesNothing() =>
        Assert.Equal(((string?)null, (string?)null), DarkModeWallpaper.OnFlip(true, Mine, null, Dark, p => p != Dark));

    [Fact]
    public void TurningDark_AlreadyDark_KeepsWhatItRemembered() =>
        Assert.Equal((Dark, Mine), DarkModeWallpaper.OnFlip(true, Dark, Mine, Dark, All));

    [Fact]
    public void TurningDark_SolidColour_RemembersNothing() =>
        Assert.Equal((Dark, (string?)null), DarkModeWallpaper.OnFlip(true, null, null, Dark, All));

    [Fact]
    public void TurningLight_RestoresRemembered() =>
        Assert.Equal((Mine, (string?)null), DarkModeWallpaper.OnFlip(false, Dark, Mine, Dark, All));

    [Fact]
    public void TurningLight_UserPickedAnotherMeanwhile_KeepsIt() =>
        Assert.Equal(((string?)null, (string?)null), DarkModeWallpaper.OnFlip(false, @"C:\other.png", Mine, Dark, All));

    [Fact]
    public void TurningLight_RememberedFileGone_LeavesWallpaper() =>
        Assert.Equal(((string?)null, (string?)null), DarkModeWallpaper.OnFlip(false, Dark, Mine, Dark, p => p != Mine));

    [Fact]
    public void TurningLight_NothingRemembered_LeavesWallpaper() =>
        Assert.Equal(((string?)null, (string?)null), DarkModeWallpaper.OnFlip(false, Dark, null, Dark, All));

    [Fact]
    public void PathsCompareIgnoringCase() =>
        Assert.Equal((Mine, (string?)null), DarkModeWallpaper.OnFlip(false, Dark.ToUpperInvariant(), Mine, Dark, All));

    // The remembered wallpaper is a file on this PC: never taken from, or compared with, another PC.
    [Fact]
    public void Sync_KeepsMyRememberedWallpaper()
    {
        var mine = new ShellSettings { WallpaperBeforeDark = Mine };
        var theirs = new ShellSettings { WallpaperBeforeDark = @"D:\theirs.png" };
        Assert.Equal(Mine, SyncRule.Merge(mine, theirs).WallpaperBeforeDark);
        Assert.Null(SyncRule.Shared(mine).WallpaperBeforeDark);
    }
}
