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
}
