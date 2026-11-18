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
}
