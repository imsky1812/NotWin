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
        Assert.Equal(14 * DotDrain.RadiusFactor, d.R, 6);
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
        Assert.All(dots, d => Assert.Equal(14 * DotDrain.OutlineFactor, d.R, 6));
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

    // Review: a bright 1920 × 1080 window at pitch 14 is ~10,000 dots; four at once stutter. Cap cells at ~4,000.
    [Fact]
    public void StepFor_CapsCellsAt4000()
    {
        var step = DotDrain.StepFor(1920, 1080, 14);
        Assert.True((1920 / step) * (1080 / step) <= DotDrain.MaxCells);
        Assert.Equal(14, DotDrain.StepFor(400, 300, 14)); // small windows keep the pitch
    }

    // Review: a maximized window's normal rectangle is its smaller restored size; it drains from the work area.
    [Fact]
    public void StartRect_MaximizedUsesTheWorkArea()
    {
        var normal = new NothingShell.Core.RectI(100, 100, 900, 700);
        var work = new NothingShell.Core.RectI(0, 0, 1920, 1032);
        Assert.Equal(work, DotDrain.StartRect(normal, maximized: true, work));
        Assert.Equal(normal, DotDrain.StartRect(normal, maximized: false, work));
    }

    // User, 2026-11-21: a real genie — the window bends into a funnel toward the icon, bottom first, then slides in.
    private static readonly NothingShell.Core.RectI Win = new(400, 100, 1200, 700); // 800 × 600
    private const double Ix = 960, Iy = 1050, Iw = 44;

    private static DrainDot Dot(double u, double v) => new(Win.Left + u * 800, Win.Top + v * 600, 6, 0);

    [Fact]
    public void Genie_StartsWhereTheWindowIs()
    {
        var g = DotDrain.Genie(Dot(0.2, 0.3), Win, Ix, Iy, Iw, 0);
        Assert.Equal((560.0, 280.0, 1.0, true), (Math.Round(g.X, 6), Math.Round(g.Y, 6), Math.Round(g.Scale, 6), g.Visible));
    }

    [Fact]
    public void Genie_EndsInsideTheIcon()
    {
        foreach (var (u, v) in new[] { (0.0, 0.0), (1.0, 0.0), (0.5, 0.5), (0.0, 1.0), (1.0, 1.0) })
            Assert.False(DotDrain.Genie(Dot(u, v), Win, Ix, Iy, Iw, 1).Visible);
    }

    [Fact]
    public void Genie_BottomBendsFirst()
    {
        // Early on, the bottom row is already pinched toward the icon while the top row is still nearly its full width.
        double Width(double v, double p) => DotDrain.Genie(Dot(1, v), Win, Ix, Iy, Iw, p).X - DotDrain.Genie(Dot(0, v), Win, Ix, Iy, Iw, p).X;
        Assert.True(Width(1, 0.3) < Width(0, 0.3) * 0.8);
        Assert.True(Width(0, 0.3) > 700);
    }

    [Fact]
    public void Genie_FallsTowardTheIcon_NeverBackUp()
    {
        var last = double.MinValue;
        for (var i = 0; i <= 20; i++)
        {
            var g = DotDrain.Genie(Dot(0.3, 0.2), Win, Ix, Iy, Iw, i / 20.0);
            if (!g.Visible) break;
            Assert.True(g.Y >= last - 1e-9);
            last = g.Y;
        }
    }

    [Fact]
    public void Genie_ColumnsStayInOrder()
    {
        // A funnel, not a scatter: left stays left of right in every row at every moment.
        for (var i = 1; i < 10; i++)
        {
            var p = i / 10.0;
            var left = DotDrain.Genie(Dot(0.25, 0.6), Win, Ix, Iy, Iw, p);
            var right = DotDrain.Genie(Dot(0.75, 0.6), Win, Ix, Iy, Iw, p);
            if (left.Visible && right.Visible) Assert.True(left.X <= right.X);
        }
    }
}
