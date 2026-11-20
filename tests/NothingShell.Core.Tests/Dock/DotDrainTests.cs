using NothingShell.Core.Dock;

namespace NothingShell.Core.Tests.Dock;

public sealed class DotDrainTests
{
    [Fact]
    public void IsNearBlack_97Percent()
    {
        var almost = new byte[1000];
        for (var i = 0; i < 30; i++) almost[i] = 200;          // 970 dark of 1000
        Assert.True(DotDrain.IsNearBlack(almost));
        var lighter = new byte[1000];
        for (var i = 0; i < 40; i++) lighter[i] = 200;         // 960 dark
        Assert.False(DotDrain.IsNearBlack(lighter));
        Assert.True(DotDrain.IsNearBlack([]));
    }

    [Fact]
    public void FromImage_BrightIsBigger_DarkDropped()
    {
        // 2 × 1 grid cells: one bright, one black (the image is given one grey byte per cell).
        var dots = DotDrain.FromImage([255, 0], 2, 1, originX: 0, originY: 0, pitch: 14, targetX: 0, targetY: 500);
        var d = Assert.Single(dots);
        Assert.Equal(7, d.X);
        Assert.Equal(7, d.Y);
        Assert.Equal(14 * 0.42, d.R, 6);
    }

    [Fact]
    public void FromImage_NearestStartsFirst()
    {
        var gray = Enumerable.Repeat((byte)255, 4 * 3).ToArray();
        var dots = DotDrain.FromImage(gray, 4, 3, 0, 0, 14, targetX: 0, targetY: 1000); // icon far below-left
        var nearest = dots.OrderBy(d => Math.Pow(d.X, 2) + Math.Pow(1000 - d.Y, 2)).First();
        var farthest = dots.OrderByDescending(d => Math.Pow(d.X, 2) + Math.Pow(1000 - d.Y, 2)).First();
        Assert.Equal(0, nearest.Delay, 6);
        Assert.Equal(DotDrain.MaxStaggerMs, farthest.Delay, 6);
    }

    [Fact]
    public void At_EndsOnTheIconSmall()
    {
        var d = new DrainDot(100, 100, 5, 40);
        var (x, y, scale, _) = DotDrain.At(d, 800, 1000, DotDrain.TotalMs, restore: false);
        Assert.Equal(800, x, 3);
        Assert.Equal(1000, y, 3);
        Assert.Equal(DotDrain.ArrivalScale, scale, 6);
    }

    [Fact]
    public void At_BeforeDelay_Stays()
    {
        var d = new DrainDot(100, 100, 5, 120);
        var (x, y, scale, _) = DotDrain.At(d, 800, 1000, 100, restore: false);
        Assert.Equal((100.0, 100.0, 1.0), (x, y, scale));
    }

    [Fact]
    public void Restore_IsTheReverse()
    {
        var d = new DrainDot(100, 100, 5, 40);
        var start = DotDrain.At(d, 800, 1000, 0, restore: true);
        Assert.Equal((800.0, 1000.0), (Math.Round(start.X, 3), Math.Round(start.Y, 3)));
        var end = DotDrain.At(d, 800, 1000, DotDrain.RestoreMs, restore: true);
        Assert.Equal((100.0, 100.0, 1.0), (Math.Round(end.X, 3), Math.Round(end.Y, 3), Math.Round(end.Scale, 6)));
    }

    [Fact]
    public void Outline_FillsEveryCell()
    {
        var dots = DotDrain.Outline(0, 0, 56, 28, 14, 0, 500); // 4 × 2 cells
        Assert.Equal(8, dots.Count);
        Assert.All(dots, d => Assert.Equal(14 * 0.30, d.R, 6));
    }

    [Fact]
    public void Gate_AllowsFourAtOnce()
    {
        var gate = new DrainGate();
        for (var i = 0; i < 4; i++) Assert.True(gate.TryStart());
        Assert.False(gate.TryStart());
        gate.End();
        Assert.True(gate.TryStart());
        Assert.Equal(4, gate.Running);
    }
}
