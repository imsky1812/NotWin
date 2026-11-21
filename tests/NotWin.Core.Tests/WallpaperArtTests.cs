using NotWin.Core.Wallpapers;

namespace NotWin.Core.Tests;

public sealed class WallpaperArtTests
{
    private const int W = 1920, H = 1080;

    [Fact]
    public void SameRecipe_SameDots()
    {
        var r = new WallRecipe(WallStyle.HalftoneLight, 42);
        Assert.Equal(WallpaperArt.Dots(r, W, H), WallpaperArt.Dots(r, W, H));
    }

    [Fact]
    public void DifferentSeeds_DifferentDots() =>
        Assert.NotEqual(WallpaperArt.Dots(new WallRecipe(WallStyle.HalftoneDark, 1), W, H),
                        WallpaperArt.Dots(new WallRecipe(WallStyle.HalftoneDark, 2), W, H));

    [Theory]
    [InlineData(WallStyle.HalftoneLight)]
    [InlineData(WallStyle.HalftoneDark)]
    [InlineData(WallStyle.Glyph)]
    [InlineData(WallStyle.Grid)]
    public void EveryStyle_OneRedDot_AllDotsOnScreen_EnoughDots(WallStyle style)
    {
        for (var seed = 0; seed < 5; seed++)
        {
            var dots = WallpaperArt.Dots(new WallRecipe(style, seed), W, H);
            Assert.Single(dots, d => d.Red);
            Assert.True(dots.Count >= 100, $"{style} seed {seed}: {dots.Count} dots");
            Assert.All(dots, d =>
            {
                Assert.InRange(d.X, 0, W);
                Assert.InRange(d.Y, 0, H);
                Assert.True(d.R > 0);
            });
        }
    }

    [Fact]
    public void LightStylesAreLight_DarkStylesAreDark()
    {
        Assert.True(WallpaperArt.Background(WallStyle.HalftoneLight) >= 200);
        Assert.True(WallpaperArt.Background(WallStyle.Grid) >= 200);
        Assert.True(WallpaperArt.Background(WallStyle.HalftoneDark) <= 20);
        Assert.True(WallpaperArt.Background(WallStyle.Glyph) <= 20);
    }

    [Fact]
    public void Random_CoversEveryStyle()
    {
        var rng = new Random(5);
        var seen = Enumerable.Range(0, 200).Select(_ => WallpaperArt.Random(rng).Style).ToHashSet();
        Assert.Equal(Enum.GetValues<WallStyle>().Length, seen.Count);
    }

    [Fact]
    public void SmallScreen_StillDrawsAWallpaper()
    {
        var dots = WallpaperArt.Dots(new WallRecipe(WallStyle.HalftoneLight, 3), 800, 600);
        Assert.Single(dots, d => d.Red);
        Assert.True(dots.Count >= 50);
    }
}
