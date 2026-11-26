using System.Windows;
using System.Windows.Media;
using NotWin.Theme;

namespace NotWin.Tests;

/// <summary>Plan 13 follow-up (user, 2026-11-26): the dock follows Windows' light / dark mode too.</summary>
public sealed class ShellThemeTests
{
    private static Color C(ResourceDictionary r, string key) => ((SolidColorBrush)r[key]).Color;

    [Fact]
    public void Light_SoftGreyDockWithDarkGlyphs()
    {
        var r = new ResourceDictionary();
        ShellTheme.Apply(r, light: true);
        Assert.Equal(Color.FromRgb(0xED, 0xED, 0xED), C(r, "Dock.Bg"));
        Assert.Equal(Color.FromRgb(0x10, 0x10, 0x10), C(r, "Dock.Text"));
        foreach (var key in ShellTheme.Keys) Assert.True(((Brush)r[key]).IsFrozen, key);
    }

    [Fact]
    public void Dark_IsTodaysBlackDock()
    {
        var r = new ResourceDictionary();
        ShellTheme.Apply(r, light: true);
        ShellTheme.Apply(r, light: false);
        Assert.Equal(Color.FromRgb(0x00, 0x00, 0x00), C(r, "Dock.Bg"));
        Assert.Equal(Color.FromRgb(0xE8, 0xE8, 0xE8), C(r, "Dock.Text"));
        Assert.Equal(ShellTheme.Keys.Count, ShellTheme.Keys.Count(r.Contains));
    }
}
