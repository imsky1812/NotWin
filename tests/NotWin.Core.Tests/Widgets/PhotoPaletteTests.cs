using NotWin.Core.Widgets;

namespace NotWin.Core.Tests.Widgets;

/// <summary>Plan 13 follow-up (user 2026-11-26): the Lucent wallpaper takes its colours from the photo frame's photo.</summary>
public sealed class PhotoPaletteTests
{
    // A width × height BGRA image filled by a function of (x, y).
    private static byte[] Image(int w, int h, Func<int, int, (byte R, byte G, byte B)> at)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (r, g, b) = at(x, y);
                var i = (y * w + x) * 4;
                px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
            }
        return px;
    }

    [Fact]
    public void Extract_RedPhoto_RedFirst()
    {
        // 80% strong red, 20% blue sky.
        var px = Image(20, 20, (x, _) => x < 16 ? ((byte)210, (byte)20, (byte)25) : ((byte)40, (byte)110, (byte)220));
        var colours = PhotoPalette.Extract(px, 20, 20);
        Assert.Equal(3, colours.Count);
        Assert.True(colours[0].R > 150 && colours[0].G < 80, $"{colours[0]}");
        Assert.Contains(colours, c => c.B > 150 && c.R < 100);
    }

    [Fact]
    public void Extract_GreyPhoto_GivesGreys()
    {
        var px = Image(10, 10, (x, y) => ((byte)(100 + x), (byte)(100 + x), (byte)(100 + x)));
        var colours = PhotoPalette.Extract(px, 10, 10);
        Assert.Equal(3, colours.Count);
        Assert.All(colours, c => Assert.True(Math.Abs(c.R - c.G) < 6 && Math.Abs(c.G - c.B) < 6, $"{c}"));
    }

    [Fact]
    public void Extract_IgnoresNearBlackAndWhite()
    {
        // Mostly black and white, a small green patch: green still leads.
        var px = Image(20, 20, (x, y) => x < 3 ? ((byte)40, (byte)170, (byte)60) : y % 2 == 0 ? ((byte)2, (byte)2, (byte)2) : ((byte)252, (byte)252, (byte)252));
        Assert.True(PhotoPalette.Extract(px, 20, 20)[0].G > 120);
    }

    [Fact]
    public void Lucent_Light_IsPastel_Dark_IsDeep()
    {
        (byte, byte, byte)[] colours = [(210, 20, 25), (40, 110, 220), (40, 170, 60)];
        var light = PhotoPalette.Lucent(colours, light: true);
        Assert.All(light.Blobs, c => Assert.True(Math.Min(c.R, Math.Min(c.G, c.B)) >= 170, $"{c}"));
        Assert.True(light.Blobs[0].R > light.Blobs[0].G, "the red photo still reads red");
        var dark = PhotoPalette.Lucent(colours, light: false);
        Assert.All(dark.Blobs, c => Assert.True(Math.Max(c.R, Math.Max(c.G, c.B)) <= 90, $"{c}"));
        Assert.True(dark.Blobs[0].R > dark.Blobs[0].B);
        Assert.True(light.Base.R > 200 && dark.Base.R < 40);
    }

    [Fact]
    public void Extract_TinyOrEmpty_IsSafe()
    {
        Assert.Equal(3, PhotoPalette.Extract([], 0, 0).Count);
        Assert.Equal(3, PhotoPalette.Extract(new byte[4], 1, 1).Count);
    }
}

/// <summary>The tinted wallpaper only ever replaces NotWin's own Lucent wallpapers, never one the user picked.</summary>
public sealed class LucentWallpaperTests
{
    [Theory]
    [InlineData(@"C:\Program Files\NotWin\Wallpapers\nothing_lucent_light.png", true)]
    [InlineData(@"C:\Program Files\NotWin\Wallpapers\NOTHING_LUCENT_DARK.PNG", true)]
    [InlineData(@"C:\Users\me\AppData\Roaming\NotWin\wallpapers\lucent_tint_light_a.png", true)]
    [InlineData(@"C:\Users\me\Pictures\beach.jpg", false)]
    [InlineData(@"C:\Program Files\NotWin\Wallpapers\nothing_halftone_dark.png", false)]
    [InlineData(null, false)]
    public void Owns(string? current, bool expected) => Assert.Equal(expected, LucentWallpaper.Owns(current));

    [Fact]
    public void FileName_AlternatesSoWindowsReloadsIt()
    {
        Assert.Equal("lucent_tint_light_a.png", LucentWallpaper.FileName(light: true, current: null));
        Assert.Equal("lucent_tint_dark_b.png", LucentWallpaper.FileName(light: false, current: @"C:\x\lucent_tint_light_a.png"));
        Assert.Equal("lucent_tint_dark_a.png", LucentWallpaper.FileName(light: false, current: @"C:\x\lucent_tint_dark_b.png"));
    }
}
