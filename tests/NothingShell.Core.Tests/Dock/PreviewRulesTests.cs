using NothingShell.Core.Dock;

namespace NothingShell.Core.Tests.Dock;

public sealed class PreviewRulesTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Fit_KeepsAspect()
    {
        Assert.Equal((220.0, 123.75), PreviewRules.Fit(1920, 1080));
        var (w, h) = PreviewRules.Fit(800, 1200);
        Assert.Equal(93.333, w, 3);
        Assert.Equal(140, h, 6);
        Assert.Equal((220.0, 140.0), PreviewRules.Fit(0, 0));
    }

    [Fact]
    public void Hover_OpensAfter400()
    {
        var h = new PreviewHover();
        h.Pointer("chrome", false, Ms(0));
        Assert.False(h.Update(Ms(399)));
        Assert.Null(h.OpenFor);
        Assert.True(h.Update(Ms(400)));
        Assert.Equal("chrome", h.OpenFor);
    }

    [Fact]
    public void Hover_SweepDoesNotOpen()
    {
        var h = new PreviewHover();
        h.Pointer("chrome", false, Ms(0));
        h.Pointer(null, false, Ms(300));
        h.Update(Ms(1000));
        Assert.Null(h.OpenFor);
    }

    [Fact]
    public void Hover_ItemToCard_StaysOpen()
    {
        var h = new PreviewHover();
        h.Pointer("chrome", false, Ms(0));
        h.Update(Ms(400));
        h.Pointer(null, true, Ms(500));   // on the card
        h.Update(Ms(2000));
        Assert.Equal("chrome", h.OpenFor);
    }

    [Fact]
    public void Hover_ClosesAfter300()
    {
        var h = new PreviewHover();
        h.Pointer("chrome", false, Ms(0));
        h.Update(Ms(400));
        h.Pointer(null, false, Ms(500));
        Assert.False(h.Update(Ms(799)));
        Assert.Equal("chrome", h.OpenFor);
        Assert.True(h.Update(Ms(800)));
        Assert.Null(h.OpenFor);
    }

    [Fact]
    public void Hover_ItemToItem_Switches()
    {
        var h = new PreviewHover();
        h.Pointer("chrome", false, Ms(0));
        h.Update(Ms(400));
        h.Pointer("spotify", false, Ms(500));
        Assert.True(h.Update(Ms(500)));
        Assert.Equal("spotify", h.OpenFor);
    }

    [Fact]
    public void Close_ClosesNow()
    {
        var h = new PreviewHover();
        h.Pointer("chrome", false, Ms(0));
        h.Update(Ms(400));
        h.Close();
        Assert.Null(h.OpenFor);
        h.Update(Ms(900)); // still on the item: a click closed it, it does not reopen until the pointer leaves and returns
        Assert.Null(h.OpenFor);
    }

    [Fact]
    public void NextCheckAt_PointsAtTheNextDeadline()
    {
        var h = new PreviewHover();
        h.Pointer("chrome", false, Ms(100));
        Assert.Equal(Ms(500), h.NextCheckAt);
        h.Update(Ms(500));
        h.Pointer(null, false, Ms(600));
        Assert.Equal(Ms(900), h.NextCheckAt);
    }

    // Review: after the card closed because its last window went away, hovering the app again must reopen it.
    [Fact]
    public void Reset_ForgetsEverything_HoverReopens()
    {
        var h = new PreviewHover();
        h.Pointer("chrome", false, Ms(0));
        h.Update(Ms(400));
        h.Reset();
        Assert.Null(h.OpenFor);
        h.Pointer("chrome", false, Ms(1000));
        h.Update(Ms(1400));
        Assert.Equal("chrome", h.OpenFor);
    }
}
