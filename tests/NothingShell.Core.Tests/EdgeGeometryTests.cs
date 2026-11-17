using NothingShell.Core;

namespace NothingShell.Core.Tests;

public sealed class EdgeGeometryTests
{
    [Fact]
    public void IsAtBottomEdge_UsesCurrentMonitorBounds()
    {
        var p = new PointI(500, 1079);
        Assert.True(EdgeGeometry.IsAtBottomEdge(p, new RectI(0, 0, 1920, 1080)));
        Assert.False(EdgeGeometry.IsAtBottomEdge(p, new RectI(0, 0, 2560, 1440)));
    }

    [Fact]
    public void IsAtBottomEdge_RequiresXInsideMonitor()
    {
        var mon = new RectI(0, 0, 1920, 1080);
        Assert.True(EdgeGeometry.IsAtBottomEdge(new PointI(0, 1079), mon));
        Assert.True(EdgeGeometry.IsAtBottomEdge(new PointI(1919, 1079), mon));
        Assert.False(EdgeGeometry.IsAtBottomEdge(new PointI(1920, 1079), mon));
        Assert.False(EdgeGeometry.IsAtBottomEdge(new PointI(-1, 1079), mon));
    }

    [Fact]
    public void IsAtBottomEdge_OneRowAboveIsNotEdge()
    {
        Assert.False(EdgeGeometry.IsAtBottomEdge(new PointI(10, 1078), new RectI(0, 0, 1920, 1080)));
    }

    [Fact]
    public void IsAtBottomEdge_SecondaryMonitorWithOffset()
    {
        var mon = new RectI(1920, 0, 3840, 1080);
        Assert.True(EdgeGeometry.IsAtBottomEdge(new PointI(2000, 1080), mon));
        Assert.False(EdgeGeometry.IsAtBottomEdge(new PointI(100, 1079), mon));
    }

    [Fact]
    public void DockBounds_CentersAboveBottomGap()
    {
        var b = EdgeGeometry.DockBounds(new RectI(0, 0, 1920, 1080), 200);
        Assert.Equal(new RectI(860, 1008, 1060, 1068), b);
    }

    [Fact]
    public void DockBounds_OffsetMonitor_AndOddWidth()
    {
        // Odd leftover space floors toward the left; width is preserved.
        var b = EdgeGeometry.DockBounds(new RectI(1920, 0, 3840, 1080), 201, 60, 12);
        Assert.Equal(201, b.Right - b.Left);
        Assert.Equal(1920 + 859, b.Left);
        Assert.Equal(60, b.Bottom - b.Top);
        Assert.Equal(1068, b.Bottom);
    }

    [Fact]
    public void DockBounds_FractionalDipsRoundToNearest()
    {
        var b = EdgeGeometry.DockBounds(new RectI(0, 0, 1000, 500), 200.4, 59.6, 11.5);
        Assert.Equal(200, b.Right - b.Left);
        Assert.Equal(60, b.Bottom - b.Top);
        Assert.Equal(500 - 12, b.Bottom);
    }
}
