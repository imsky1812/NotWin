using NothingShell.Core;
using NothingShell.Core.Dock;

namespace NothingShell.Core.Tests.Dock;

// User, 2026-11-21: "remove dots entirely, just keep the genie animation" — the window's own picture is cut into
// horizontal strips, and each strip is placed along the genie funnel every frame.
public sealed class GenieStripsTests
{
    private static readonly RectI Win = new(400, 100, 1200, 700); // 800 × 600
    private const double Ix = 960, Iy = 1050, Iw = 44;

    [Fact]
    public void AtStart_StripsTileTheWindowExactly()
    {
        var strips = GenieStrips.Layout(Win, Ix, Iy, Iw, 0, 10);
        Assert.Equal(10, strips.Count);
        for (var i = 0; i < 10; i++)
        {
            var s = strips[i]!.Value;
            Assert.Equal(400, s.X, 6);
            Assert.Equal(800, s.Width, 6);
            Assert.Equal(100 + i * 60, s.Y, 6);
            Assert.Equal(60, s.Height, 6);
        }
    }

    [Fact]
    public void AtEnd_EverythingIsInTheIcon() =>
        Assert.All(GenieStrips.Layout(Win, Ix, Iy, Iw, 1, 10), s => Assert.Null(s));

    [Fact]
    public void MidWay_TheBottomIsPinchedTowardTheIcon()
    {
        var strips = GenieStrips.Layout(Win, Ix, Iy, Iw, 0.35, 10);
        var top = strips[0]!.Value;
        var bottom = strips[9]!.Value;
        Assert.True(bottom.Width < top.Width * 0.7);
        Assert.True(bottom.X + bottom.Width / 2 > top.X + top.Width / 2); // leaning toward the icon (right of centre)
    }

    [Fact]
    public void StripsStayInOrder_NoGapsOrOverlapsVertically()
    {
        for (var k = 0; k <= 10; k++)
        {
            var strips = GenieStrips.Layout(Win, Ix, Iy, Iw, k / 10.0, 12);
            for (var i = 1; i < strips.Count; i++)
                if (strips[i - 1] is { } a && strips[i] is { } b)
                    Assert.Equal(a.Y + a.Height, b.Y, 3);
        }
    }

    [Fact]
    public void StripsForWindow_ScalesWithItsHeight()
    {
        // One strip per ~6 px, so the bent edges look smooth (the first render showed steps at 12.5 px).
        Assert.Equal(100, GenieStrips.CountFor(600));
        Assert.Equal(24, GenieStrips.CountFor(80));    // at least 24
        Assert.Equal(120, GenieStrips.CountFor(2000)); // at most 120
    }
}
