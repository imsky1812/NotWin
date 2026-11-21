using NotWin.Core;

namespace NotWin.Core.Tests;

public sealed class DockRevealMachineTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    private static readonly DockInputs Idle = new(false, false, false, false, false);
    private static readonly DockInputs Edge = Idle with { CursorAtEdge = true };
    private static readonly DockInputs OverDock = Idle with { CursorOverDock = true };

    private static DockRevealMachine Revealed()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Edge, Ms(0));
        m.Update(Edge, Ms(150));
        Assert.True(m.Visible);
        return m;
    }

    [Fact]
    public void EdgeFor149ms_StaysHidden()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Edge, Ms(0));
        m.Update(Edge, Ms(149));
        Assert.False(m.Visible);
        Assert.Equal(Ms(150), m.NextDeadline);
    }

    [Fact]
    public void EdgeFor150ms_Reveals()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Edge, Ms(0));
        m.Update(Edge, Ms(150));
        Assert.True(m.Visible);
        Assert.Null(m.NextDeadline);
    }

    [Fact]
    public void LeaveEdgeBefore150ms_CancelsReveal()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Edge, Ms(0));
        m.Update(Idle, Ms(100));
        Assert.Null(m.NextDeadline);
        m.Update(Idle, Ms(1000));
        Assert.False(m.Visible);
    }

    [Fact]
    public void EdgeReenteredAfterCancelledReveal_RestartsTimer()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Edge, Ms(0));
        m.Update(Idle, Ms(100));
        m.Update(Edge, Ms(200));
        m.Update(Edge, Ms(349));
        Assert.False(m.Visible);
        Assert.Equal(Ms(350), m.NextDeadline);
        m.Update(Edge, Ms(350));
        Assert.True(m.Visible);
    }

    [Fact]
    public void LateUpdateAfterDeadline_StillReveals()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Edge, Ms(0));
        m.Update(Edge, Ms(900));
        Assert.True(m.Visible);
    }

    [Fact]
    public void UpdateTwiceWithSameInput_DoesNotRestartTimer()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Edge, Ms(0));
        m.Update(Edge, Ms(0));
        m.Update(Edge, Ms(100));
        Assert.Equal(Ms(150), m.NextDeadline);
    }

    [Fact]
    public void LeaveDock_HidesAfter500ms()
    {
        var m = Revealed();
        m.Update(Idle, Ms(1000));
        m.Update(Idle, Ms(1499));
        Assert.True(m.Visible);
        Assert.Equal(Ms(1500), m.NextDeadline);
        m.Update(Idle, Ms(1500));
        Assert.False(m.Visible);
        Assert.Null(m.NextDeadline);
    }

    [Fact]
    public void ReenterBefore500ms_CancelsHide()
    {
        var m = Revealed();
        m.Update(Idle, Ms(1000));
        m.Update(OverDock, Ms(1400));
        Assert.Null(m.NextDeadline);
        m.Update(OverDock, Ms(5000));
        Assert.True(m.Visible);
        // Leaving again restarts a full 500 ms.
        m.Update(Idle, Ms(5100));
        m.Update(Idle, Ms(5599));
        Assert.True(m.Visible);
        m.Update(Idle, Ms(5600));
        Assert.False(m.Visible);
    }

    [Fact]
    public void CursorAtEdgeWhileVisible_DoesNotHide()
    {
        var m = Revealed();
        m.Update(Edge, Ms(10_000));
        Assert.True(m.Visible);
        Assert.Null(m.NextDeadline);
    }

    [Fact]
    public void PanelOpen_KeepsVisible()
    {
        var m = Revealed();
        var panel = Idle with { PanelOpen = true };
        m.Update(panel, Ms(1000));
        m.Update(panel, Ms(10_000));
        Assert.True(m.Visible);
        Assert.Null(m.NextDeadline);
        // Closing the panel starts the hide timer from that moment.
        m.Update(Idle, Ms(10_100));
        Assert.True(m.Visible);
        Assert.Equal(Ms(10_600), m.NextDeadline);
        m.Update(Idle, Ms(10_600));
        Assert.False(m.Visible);
    }

    [Fact]
    public void Fullscreen_HidesAndIgnoresEdge()
    {
        var m = Revealed();
        var fs = Idle with { Fullscreen = true };
        m.Update(fs, Ms(1000));
        Assert.False(m.Visible);
        var fsEdge = fs with { CursorAtEdge = true };
        m.Update(fsEdge, Ms(1100));
        m.Update(fsEdge, Ms(5000));
        Assert.False(m.Visible);
        Assert.Null(m.NextDeadline);
    }

    [Fact]
    public void Fullscreen_PendingRevealIsDropped()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Edge, Ms(0));
        m.Update(Edge with { Fullscreen = true }, Ms(50));
        Assert.Null(m.NextDeadline);
        // Leaving fullscreen with the cursor still at the edge starts a fresh 150 ms.
        m.Update(Edge, Ms(100));
        m.Update(Edge, Ms(249));
        Assert.False(m.Visible);
        m.Update(Edge, Ms(250));
        Assert.True(m.Visible);
    }

    [Fact]
    public void Fullscreen_BeatsKeepVisible()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Idle with { KeepVisible = true }, Ms(0));
        Assert.True(m.Visible);
        m.Update(Idle with { KeepVisible = true, Fullscreen = true, CursorAtEdge = true }, Ms(10));
        Assert.False(m.Visible);
        Assert.Null(m.NextDeadline);
        // Fullscreen ends: a KeepVisible dock is back at once, no edge needed.
        m.Update(Idle with { KeepVisible = true }, Ms(20));
        Assert.True(m.Visible);
    }

    [Fact]
    public void KeepVisible_ShowsWithoutEdge()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Idle with { KeepVisible = true }, Ms(0));
        Assert.True(m.Visible);
    }

    [Fact]
    public void KeepVisibleTurnedOff_HidesAfterDelay()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Idle with { KeepVisible = true }, Ms(0));
        m.Update(Idle, Ms(100));
        Assert.True(m.Visible);
        Assert.Equal(Ms(600), m.NextDeadline);
        m.Update(Idle, Ms(600));
        Assert.False(m.Visible);
    }

    [Fact]
    public void FullscreenEntered_ThenExited_StartsHidden()
    {
        var m = Revealed();
        m.Update(Idle with { Fullscreen = true }, Ms(1000));
        m.Update(Idle, Ms(2000));
        Assert.False(m.Visible);
    }

    [Fact]
    public void NewMachine_IsHiddenWithNoDeadline()
    {
        var m = DockRevealMachine.CreateDefault();
        Assert.False(m.Visible);
        Assert.Null(m.NextDeadline);
    }

    // User, 2026-11-21: "Keep dock visible" stays up on the desktop and over normal windows; a maximized app in front
    // makes it behave like auto-hide (slides down when the mouse leaves, the bottom edge brings it back).
    [Fact]
    public void KeepVisible_MaximizedFront_HidesAfterIdle_EdgeReveals()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Idle with { KeepVisible = true }, Ms(0));
        Assert.True(m.Visible);
        var maximized = Idle with { KeepVisible = true, MaximizedFront = true };
        m.Update(maximized, Ms(100));
        Assert.True(m.Visible); // not at once: the idle delay first
        m.Update(maximized, Ms(600));
        Assert.False(m.Visible);
        m.Update(maximized with { CursorAtEdge = true }, Ms(1000));
        m.Update(maximized with { CursorAtEdge = true }, Ms(1150));
        Assert.True(m.Visible);
        // Back to a normal window: pinned visible again.
        m.Update(Idle with { KeepVisible = true }, Ms(3000));
        Assert.True(m.Visible);
        Assert.Null(m.NextDeadline);
    }

    [Fact]
    public void KeepVisible_MaximizedFront_OpenPanelStillHoldsTheDock()
    {
        var m = DockRevealMachine.CreateDefault();
        m.Update(Idle with { KeepVisible = true, MaximizedFront = true, PanelOpen = true }, Ms(0));
        m.Update(Idle with { KeepVisible = true, MaximizedFront = true, PanelOpen = true }, Ms(2000));
        Assert.True(m.Visible);
    }
}
