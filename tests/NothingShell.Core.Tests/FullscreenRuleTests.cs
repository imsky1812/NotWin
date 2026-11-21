using NothingShell.Core;

namespace NothingShell.Core.Tests;

public sealed class FullscreenRuleTests
{
    private static readonly RectI Mon = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NullForeground_KeepsPreviousState(bool previous) => Assert.Equal(previous, FullscreenRule.Decide(null, previous));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OwnProcessForeground_KeepsPreviousState(bool previous) =>
        // The launcher covers the monitor and is the foreground window: it must not count as a full-screen app.
        Assert.Equal(previous, FullscreenRule.Decide(new FullscreenProbe(Mon, Mon, "HwndWrapper[NothingShell]", false, true), previous, foregroundIsOwnProcess: true));

    [Fact]
    public void Decide_WithProbe_UsesRule() =>
        Assert.True(FullscreenRule.Decide(new FullscreenProbe(Mon, Mon, "UnityWndClass", false, false), previous: false));

    [Fact]
    public void BorderlessGameCoveringMonitor_IsFullscreen() =>
        Assert.True(FullscreenRule.IsFullscreen(new FullscreenProbe(Mon, Mon, "UnityWndClass", false, false)));

    // Measured 2026-11-18: YouTube full screen in a maximized Chrome keeps IsZoomed=true; frame = monitor; style loses
    // WS_CAPTION and WS_THICKFRAME (0x17CF0000 -> 0x170B0000). SHQueryUserNotificationState says BUSY only for a while;
    // once it stops, the old rule ("maximized is never full screen") flipped the state back and the dock came up.
    [Fact]
    public void MaximizedBorderlessWindowCoveringMonitor_IsFullscreen_WithoutQuns() =>
        Assert.True(FullscreenRule.IsFullscreen(new FullscreenProbe(Mon, Mon, "Chrome_WidgetWin_1", IsMaximized: true, QunsSaysFullscreen: false, HasFrame: false)));

    [Fact]
    public void MaximizedBorderlessWindowNotCoveringMonitor_IsNot() =>
        Assert.False(FullscreenRule.IsFullscreen(new FullscreenProbe(new RectI(0, 0, 1920, 975), Mon, "Chrome_WidgetWin_1", IsMaximized: true, QunsSaysFullscreen: false, HasFrame: false)));

    [Fact]
    public void MaximizedWindowCoveringMonitor_IsNotFullscreen() =>
        Assert.False(FullscreenRule.IsFullscreen(new FullscreenProbe(Mon, Mon, "Chrome_WidgetWin_1", true, false)));

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    public void DesktopCoveringMonitor_IsNot(string cls) =>
        Assert.False(FullscreenRule.IsFullscreen(new FullscreenProbe(Mon, Mon, cls, false, false)));

    [Fact]
    public void QunsD3D_IsFullscreenEvenIfRectDiffers() =>
        Assert.True(FullscreenRule.IsFullscreen(new FullscreenProbe(new RectI(100, 100, 800, 600), Mon, "Game", false, true)));

    [Fact]
    public void SmallWindow_IsNotFullscreen() =>
        Assert.False(FullscreenRule.IsFullscreen(new FullscreenProbe(new RectI(100, 100, 800, 600), Mon, "Notepad", false, false)));

    // User, 2026-11-21: a maximized app in front on the dock's monitor hides the dock; the desktop and other monitors
    // never do.
    [Fact]
    public void MaximizedOn_OnlyAMaximizedWindowOnTheDockMonitor()
    {
        var work = new RectI(0, 0, 1920, 1080);
        var other = new RectI(1920, 0, 3840, 1080);
        Assert.True(FullscreenRule.IsMaximizedOn(new FullscreenProbe(work, Mon, "Chrome_WidgetWin_1", true, false), Mon));
        Assert.False(FullscreenRule.IsMaximizedOn(new FullscreenProbe(new RectI(100, 100, 900, 700), Mon, "Chrome_WidgetWin_1", false, false), Mon));
        Assert.False(FullscreenRule.IsMaximizedOn(new FullscreenProbe(other, other, "Chrome_WidgetWin_1", true, false), Mon));
        Assert.False(FullscreenRule.IsMaximizedOn(new FullscreenProbe(work, Mon, "WorkerW", true, false), Mon));
        Assert.False(FullscreenRule.IsMaximizedOn(null, Mon));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DecideMaximized_OwnProcessKeepsPrevious(bool previous) =>
        Assert.Equal(previous, FullscreenRule.DecideMaximized(new FullscreenProbe(Mon, Mon, "HwndWrapper[NothingShell]", false, false), Mon, previous, foregroundIsOwnProcess: true));

    [Fact]
    public void DecideMaximized_NoForeground_KeepsPrevious() => Assert.True(FullscreenRule.DecideMaximized(null, Mon, previous: true));
}
