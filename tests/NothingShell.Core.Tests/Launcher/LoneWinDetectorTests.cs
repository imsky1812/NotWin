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
        // Any ordinary combo (Win+E here; Win+A/N are the panel keys, covered below).
        const uint E = 0x45;
        var d = new LoneWinDetector();
        Assert.Equal(Nothing, d.OnKey(LWin, true, false));
        Assert.Equal(Nothing, d.OnKey(E, true, false));
        Assert.Equal(Nothing, d.OnKey(E, false, false));
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
    public void StaleWinDown_AfterMissedWinUp_NextTapOpens()
    {
        // Win+L: both the L and the Win keyups happen on the lock screen; the hook sees neither.
        var physicallyDown = new HashSet<uint>();
        var d = new LoneWinDetector(vk => physicallyDown.Contains(vk));
        physicallyDown.Add(LWin); d.OnKey(LWin, true, false);
        physicallyDown.Add(0x4C); d.OnKey(0x4C, true, false);
        physicallyDown.Clear(); // unlocked later: nothing is held any more

        // In an LL hook the async key state updates after the hook returns: a fresh press still reads "up".
        Assert.Equal(Nothing, d.OnKey(LWin, true, false));
        Assert.Equal(Open, d.OnKey(LWin, false, false));
    }

    private const uint KeyA = 0x41, KeyN = 0x4E, KeyE = 0x45, LCtrl = 0xA2;

    [Fact]
    public void WinA_SwallowsLetterAndOpensPanel()
    {
        var d = new LoneWinDetector();
        d.OnKey(LWin, true, false);
        Assert.Equal(new KeyVerdict(Swallow: true, InjectMaskFirst: false, OpenLauncher: false, OpenPanel: true), d.OnKey(KeyA, true, false));
        Assert.Equal(new KeyVerdict(Swallow: true, InjectMaskFirst: false, OpenLauncher: false), d.OnKey(KeyA, false, false));
        Assert.Equal(new KeyVerdict(Swallow: false, InjectMaskFirst: true, OpenLauncher: false), d.OnKey(LWin, false, false));
    }

    [Fact]
    public void WinN_SameAsWinA()
    {
        var d = new LoneWinDetector();
        d.OnKey(LWin, true, false);
        Assert.True(d.OnKey(KeyN, true, false).OpenPanel);
        Assert.True(d.OnKey(KeyN, false, false).Swallow);
        var up = d.OnKey(LWin, false, false);
        Assert.True(up.InjectMaskFirst);
        Assert.False(up.OpenLauncher);
    }

    [Fact]
    public void WinShiftA_PassesThrough()
    {
        var d = new LoneWinDetector();
        d.OnKey(LWin, true, false);
        d.OnKey(LShift, true, false);
        Assert.Equal(Nothing, d.OnKey(KeyA, true, false));
    }

    [Fact]
    public void WinCtrlN_PassesThrough()
    {
        var d = new LoneWinDetector();
        d.OnKey(LWin, true, false);
        d.OnKey(LCtrl, true, false);
        Assert.Equal(Nothing, d.OnKey(KeyN, true, false));
    }

    [Fact]
    public void AutoRepeatA_OpensOnce()
    {
        var d = new LoneWinDetector();
        d.OnKey(LWin, true, false);
        Assert.True(d.OnKey(KeyA, true, false).OpenPanel);
        var repeat = d.OnKey(KeyA, true, false);
        Assert.True(repeat.Swallow);
        Assert.False(repeat.OpenPanel);
    }

    [Fact]
    public void WinA_ThenWinE_EStillPassesThrough()
    {
        var d = new LoneWinDetector();
        d.OnKey(LWin, true, false);
        d.OnKey(KeyA, true, false);
        d.OnKey(KeyA, false, false);
        Assert.Equal(Nothing, d.OnKey(KeyE, true, false));
    }

    [Fact]
    public void LetterUpAfterWinReleased_IsStillSwallowed()
    {
        var d = new LoneWinDetector();
        d.OnKey(LWin, true, false);
        d.OnKey(KeyA, true, false);
        d.OnKey(LWin, false, false);
        Assert.True(d.OnKey(KeyA, false, false).Swallow);
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
