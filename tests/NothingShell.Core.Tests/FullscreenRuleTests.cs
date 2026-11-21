using NothingShell.Core;

namespace NothingShell.Core.Tests;

public sealed class FullscreenRuleTests
{
    private static readonly RectI Mon = new(0, 0, 1920, 1080);

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
