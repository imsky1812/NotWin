using NothingShell.Core.Notch;

namespace NothingShell.Core.Tests.Notch;

public sealed class NotchHoverTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    private static NotchHover Opened()
    {
        var h = new NotchHover();
        h.Pointer(overPill: true, overOpenArea: false, Ms(0));
        h.Update(Ms(150));
        Assert.True(h.IsOpen);
        h.Pointer(overPill: true, overOpenArea: true, Ms(150));
        return h;
    }

    [Fact]
    public void RestingOnPill_OpensAfter150ms()
    {
        var h = new NotchHover();
        h.Pointer(true, false, Ms(0));
        Assert.Equal(Ms(150), h.NextCheckAt);
        Assert.False(h.Update(Ms(149)));
        Assert.False(h.IsOpen);
        Assert.True(h.Update(Ms(150)));
        Assert.True(h.IsOpen);
        Assert.Equal(TimeSpan.FromMilliseconds(150), NotchHover.OpenDwell);
    }

    [Fact]
    public void PassingThrough_DoesNotOpen()
    {
        var h = new NotchHover();
        h.Pointer(true, false, Ms(0));
        h.Pointer(false, false, Ms(80));
        Assert.False(h.Update(Ms(200)));
        Assert.False(h.IsOpen);
        Assert.Null(h.NextCheckAt);
    }

    [Fact]
    public void Leave_ClosesAfter300ms()
    {
        var h = Opened();
        h.Pointer(false, false, Ms(1000));
        Assert.False(h.Update(Ms(1299)));
        Assert.True(h.IsOpen);
        Assert.True(h.Update(Ms(1300)));
        Assert.False(h.IsOpen);
        Assert.Equal(TimeSpan.FromMilliseconds(300), NotchHover.CloseDelay);
    }

    [Fact]
    public void Leave_ComingBackWithin300ms_StaysOpen()
    {
        var h = Opened();
        h.Pointer(false, false, Ms(1000));
        h.Pointer(false, true, Ms(1200));
        h.Update(Ms(1400));
        Assert.True(h.IsOpen);
    }

    [Fact]
    public void EditHold_KeepsOpenOutside()
    {
        var h = Opened();
        h.Hold("edit", true, Ms(500));
        h.Pointer(false, false, Ms(1000));
        h.Update(Ms(5000));
        Assert.True(h.IsOpen);
    }

    [Fact]
    public void TextFocus_HoldsOpen_ReleaseOutsideClosesLater()
    {
        var h = Opened();
        h.Hold("text", true, Ms(500));
        h.Pointer(false, false, Ms(1000));
        h.Update(Ms(5000));
        Assert.True(h.IsOpen);
        h.Hold("text", false, Ms(6000));
        Assert.Equal(Ms(6300), h.NextCheckAt);
        h.Update(Ms(6299));
        Assert.True(h.IsOpen);
        h.Update(Ms(6300));
        Assert.False(h.IsOpen);
    }

    [Fact]
    public void Hold_OnlyEditAndText_KeepOpen()
    {
        // Spec §6.4: "closes when the mouse leaves (unless editing)". An expanded list is not a hold (review #2).
        var h = Opened();
        h.Hold("expand", true, Ms(500));
        h.Pointer(false, false, Ms(1000));
        h.Update(Ms(1300));
        Assert.False(h.IsOpen);
    }

    [Fact]
    public void Fullscreen_ClosesAndBlocksOpen()
    {
        var h = Opened();
        h.SetFullscreen(true);
        Assert.False(h.IsOpen);
        h.Pointer(true, false, Ms(2000));
        h.Update(Ms(3000));
        Assert.False(h.IsOpen);
        h.SetFullscreen(false);
        h.Pointer(true, false, Ms(4000));
        h.Update(Ms(4150));
        Assert.True(h.IsOpen);
    }

    [Fact]
    public void Close_ClosesEvenWithPointerInside_ReopensOnlyAfterLeaving()
    {
        var h = Opened();
        h.Hold("edit", true, Ms(200));
        h.Close();
        Assert.False(h.IsOpen);
        h.Pointer(true, false, Ms(300));
        h.Update(Ms(1000));
        Assert.False(h.IsOpen);         // still resting on the pill: no reopen
        h.Pointer(false, false, Ms(1100));
        h.Pointer(true, false, Ms(1200));
        h.Update(Ms(1350));
        Assert.True(h.IsOpen);
    }
}
