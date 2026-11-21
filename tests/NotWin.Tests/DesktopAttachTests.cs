using NotWin.Native;

namespace NotWin.Tests;

public sealed class DesktopAttachTests
{
    private const int WsChild = 0x40000000, WsPopup = unchecked((int)0x80000000), WsCaption = 0x00C00000;
    private const int WsExAppWindow = 0x00040000, WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000;

    [Fact]
    public void ChildStyle_IsNotATopLevelWindow()
    {
        // Spike D.4 (2026-11-17): a top-level desktop widget showed in Alt+Tab. Widgets are child windows with no owner.
        var style = DesktopAttach.ChildStyle(WsPopup | WsCaption);
        Assert.Equal(WsChild, style & WsChild);
        Assert.Equal(0, style & WsPopup);
        Assert.Equal(0, style & WsCaption);

        var ex = DesktopAttach.ChildExStyle(WsExAppWindow);
        Assert.Equal(WsExToolWindow | WsExNoActivate, ex & (WsExToolWindow | WsExNoActivate));
        Assert.Equal(0, ex & WsExAppWindow);
        Assert.Equal(0x4, ex & 0x4); // WS_EX_NOPARENTNOTIFY: no synchronous WM_PARENTNOTIFY to Explorer's desktop
    }

    // Explorer (2026-11 builds) draws Progman through composition with no surface for child windows: children of
    // Progman or of the wallpaper WorkerW are never drawn. Children of SHELLDLL_DefView (the icon view) are.
    // Read-only against the live desktop; skipped in effect when there is no Explorer desktop.
    [Fact]
    public void FindHost_IsTheDesktopIconView()
    {
        var host = DesktopAttach.FindHost();
        if (host == 0) return; // no Explorer desktop (CI)
        Assert.Equal("SHELLDLL_DefView", DesktopAttach.ClassOf(host));
    }
}
