using NothingShell.Core.Dock;

namespace NothingShell.Core.Tests.Dock;

public sealed class DotDrainTests
{
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

    // Smoothness (user, 2026-11-21: "choppy"): draw only over the window-to-icon area, not the whole screen.
    [Fact]
    public void Area_CoversTheWindowAndTheIcon_Padded()
    {
        var area = DotDrain.Area(new NothingShell.Core.RectI(400, 100, 1200, 700), new NothingShell.Core.RectI(940, 1030, 984, 1074), pad: 20);
        Assert.Equal(new NothingShell.Core.RectI(380, 80, 1220, 1094), area);
    }

    [Fact]
    public void FrameStats_CountsFramesAndWorstGap()
    {
        var stats = new FrameStats();
        foreach (var t in new[] { 0.0, 16, 33, 70, 86 }) stats.At(t);
        Assert.Equal(5, stats.Frames);
        Assert.Equal(37, stats.WorstGapMs, 6);
    }

    // User picked "Colour dots" (2026-11-21): every dot has its spot's colour, muted 25 % toward grey, all the same size.
    // The genie from the preview: soft start (nothing moves in the first instant) and soft landing.
    [Fact]
    public void Genie_SoftStart()
    {
        var g = DotDrain.Genie(Dot(0.5, 0.0), Win, Ix, Iy, Iw, 0.02);
        Assert.True(Math.Abs(g.Y - 100) < 0.5);
        Assert.Equal(620, DotDrain.GenieMs);
        Assert.Equal(560, DotDrain.GenieRestoreMs);
    }
}
