using NothingShell.Core.Launcher;

namespace NothingShell.Core.Tests.Launcher;

public sealed class LoneWinDetectorTests
{
    private const uint LWin = 0x5B, RWin = 0x5C, A = 0x41, LShift = 0xA0, Mask = 0xE8;
    private static readonly KeyVerdict Nothing = new(false, false, false);
    private static readonly KeyVerdict Open = new(Swallow: false, InjectMaskFirst: true, OpenLauncher: true);

    [Fact]
    public void LoneTap_OpensLauncher()
    {
        var d = new LoneWinDetector();
        Assert.Equal(Nothing, d.OnKey(LWin, down: true, injectedByUs: false));
        Assert.Equal(Open, d.OnKey(LWin, down: false, injectedByUs: false));
    }

    [Fact]
    public void RightWin_AlsoOpens()
    {
        var d = new LoneWinDetector();
        d.OnKey(RWin, true, false);
        Assert.Equal(Open, d.OnKey(RWin, false, false));
    }

    [Fact]
    public void ChordedWin_DoesNotOpen()
    {
        var d = new LoneWinDetector();
        Assert.Equal(Nothing, d.OnKey(LWin, true, false));
        Assert.Equal(Nothing, d.OnKey(A, true, false));
        Assert.Equal(Nothing, d.OnKey(A, false, false));
        Assert.Equal(Nothing, d.OnKey(LWin, false, false));
    }

    [Fact]
    public void ShiftThenWin_DoesNotOpen()
    {
        var d = new LoneWinDetector();
        d.OnKey(LShift, true, false);
        d.OnKey(LWin, true, false);
        Assert.Equal(Nothing, d.OnKey(LWin, false, false));
        d.OnKey(LShift, false, false);
    }

    [Fact]
    public void WinAfterOtherKeyHeld_DoesNotOpen()
    {
        var d = new LoneWinDetector();
        d.OnKey(A, true, false);
        d.OnKey(LWin, true, false);
        Assert.Equal(Nothing, d.OnKey(LWin, false, false));
    }

    [Fact]
    public void DoubleTap_OpensTwice()
    {
        var d = new LoneWinDetector();
        d.OnKey(LWin, true, false);
        Assert.Equal(Open, d.OnKey(LWin, false, false));
        d.OnKey(LWin, true, false);
        Assert.Equal(Open, d.OnKey(LWin, false, false));
    }

    [Fact]
    public void AutoRepeatWinDown_StillLone()
    {
        var d = new LoneWinDetector();
        d.OnKey(LWin, true, false);
        d.OnKey(LWin, true, false);
        d.OnKey(LWin, true, false);
        Assert.Equal(Open, d.OnKey(LWin, false, false));
    }

    [Fact]
    public void StaleHeldKey_AfterMissedKeyUp_DoesNotBlockForever()
    {
        // Win+L: the L keyup happens on the secure desktop, so the hook never sees it.
        var physicallyDown = new HashSet<uint>();
        var d = new LoneWinDetector(vk => physicallyDown.Contains(vk));
        physicallyDown.Add(LWin); d.OnKey(LWin, true, false);
        physicallyDown.Add(0x4C); d.OnKey(0x4C, true, false);
        physicallyDown.Clear(); // keyups missed while locked
        d.OnKey(LWin, false, false);

        physicallyDown.Add(LWin);
        d.OnKey(LWin, true, false);
        physicallyDown.Remove(LWin);
        Assert.Equal(Open, d.OnKey(LWin, false, false));
    }

    [Fact]
    public void InjectedOwnKeys_AreIgnored()
    {
        var d = new LoneWinDetector();
        d.OnKey(LWin, true, false);
        Assert.Equal(Nothing, d.OnKey(Mask, true, injectedByUs: true));
        Assert.Equal(Nothing, d.OnKey(Mask, false, injectedByUs: true));
        Assert.Equal(Open, d.OnKey(LWin, false, false));
    }
}
