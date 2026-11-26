using System.Windows;
using System.Windows.Media;
using NotWin.Theme;

namespace NotWin.Tests;

/// <summary>Plan 13 follow-up (user, 2026-11-26): the dock follows Windows' light / dark mode too.</summary>
public sealed class ShellThemeTests
{
    private static Color C(ResourceDictionary r, string key) => ((SolidColorBrush)r[key]).Color;

    [Fact]
    public void Light_FrostedWhiteDockWithDarkGlyphs()
    {
        var r = new ResourceDictionary();
        ShellTheme.Apply(r, light: true);
        Assert.Equal(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), C(r, "Dock.Bg")); // frosted white (Lucent)
        Assert.Equal(Color.FromRgb(0x10, 0x10, 0x10), C(r, "Dock.Text"));
        foreach (var key in ShellTheme.Keys) Assert.True(((Brush)r[key]).IsFrozen, key);
    }

    // User 2026-11-26: the notch, quick settings (Win+A / Win+N) and the dock's window preview follow the theme too.
    [Fact]
    public void Light_ShellSurfacesAreLightWithInk()
    {
        var r = new ResourceDictionary();
        ShellTheme.Apply(r, light: true);
        Assert.Equal(Color.FromArgb(0xF2, 0xF7, 0xF7, 0xF7), C(r, "Shell.Glass"));
        Assert.Equal(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF), C(r, "Shell.Pill"));
        Assert.Equal(Color.FromRgb(0x10, 0x10, 0x10), C(r, "Shell.Text"));
        Assert.Equal(Color.FromRgb(0x1A, 0x1A, 0x1A), C(r, "Shell.OnBg")); // an "on" tile is ink in light mode
        ShellTheme.Apply(r, light: false);
        Assert.Equal(Color.FromRgb(0x00, 0x00, 0x00), C(r, "Shell.Pill"));
        Assert.Equal(Color.FromRgb(0xE8, 0xE8, 0xE8), C(r, "Shell.OnBg"));
    }

    [Fact]
    public void Dark_IsTodaysBlackDock()
    {
        var r = new ResourceDictionary();
        ShellTheme.Apply(r, light: true);
        ShellTheme.Apply(r, light: false);
        Assert.Equal(Color.FromArgb(0xE6, 0x00, 0x00, 0x00), C(r, "Dock.Bg"));
        Assert.Equal(Color.FromRgb(0xE8, 0xE8, 0xE8), C(r, "Dock.Text"));
        Assert.Equal(ShellTheme.Keys.Count, ShellTheme.Keys.Count(r.Contains));
    }
}
