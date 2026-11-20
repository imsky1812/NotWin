using NothingShell.Core;
using NothingShell.Core.Dock;

namespace NothingShell.Core.Tests.Dock;

// User, 2026-11-21 (option A): the window's live picture shrinks smoothly into its dock icon — drawn by Windows itself.
public sealed class ShrinkMotionTests
{
    private static readonly RectI Win = new(400, 100, 1200, 700); // 800 × 600
    private static readonly RectI Icon = new(940, 1030, 984, 1074); // 44 px, centre (962, 1052)

    [Fact]
    public void StartsAsTheWindow_FullyVisible()
    {
        var f = ShrinkMotion.At(Win, Icon, 0);
        Assert.Equal((400.0, 100.0, 800.0, 600.0, 1.0), (f.X, f.Y, f.W, f.H, f.Opacity));
    }

    [Fact]
    public void EndsOnTheIcon_IconWide_Faded()
    {
        var f = ShrinkMotion.At(Win, Icon, 1);
        Assert.Equal(44, f.W, 6);
        Assert.Equal(962, f.X + f.W / 2, 6);
        Assert.Equal(1052, f.Y + f.H / 2, 6);
        Assert.Equal(0, f.Opacity, 6);
    }

    [Fact]
    public void ShrinksSteadily_KeepingItsShape()
    {
        var last = double.MaxValue;
        for (var i = 0; i <= 20; i++)
        {
            var f = ShrinkMotion.At(Win, Icon, i / 20.0);
            Assert.True(f.W <= last + 1e-9);
            Assert.Equal(800.0 / 600.0, f.W / f.H, 6);
            last = f.W;
        }
    }

    [Fact]
    public void StaysVisibleMostOfTheWay()
    {
        Assert.Equal(1, ShrinkMotion.At(Win, Icon, 0.6).Opacity, 6);
        Assert.True(ShrinkMotion.At(Win, Icon, 0.9).Opacity < 0.5);
    }
}
