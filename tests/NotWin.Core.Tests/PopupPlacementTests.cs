namespace NotWin.Core.Tests;

// Plan 10: a widget's editor opens next to it on the widget's own screen (not clamped to the main one).
public sealed class PopupPlacementTests
{
    private static readonly RectI Second = new(1920, 0, 3840, 1080);

    [Fact]
    public void NextTo_RightOfTheWidget_OnItsScreen() =>
        Assert.Equal((2230, 100), PopupPlacement.NextTo(new RectI(2000, 100, 2220, 320), Second, 300, 200, 10));

    [Fact]
    public void NextTo_NoRoomOnTheRight_GoesLeft() =>
        Assert.Equal((3290, 100), PopupPlacement.NextTo(new RectI(3600, 100, 3820, 320), Second, 300, 200, 10));

    [Fact]
    public void NextTo_KeptInsideTheScreenVertically() =>
        Assert.Equal((2230, 880), PopupPlacement.NextTo(new RectI(2000, 1000, 2220, 1070), Second, 300, 200, 10));

    // Plan 13 review: the choice list is anchored to a widget, which may sit low on the desktop.
    private static readonly RectI Main = new(0, 0, 1920, 1080);

    [Fact]
    public void Below_CentredUnderTheAnchor() =>
        Assert.Equal((150, 318), PopupPlacement.Below(new RectI(100, 100, 400, 310), Main, 200, 300, 8));

    [Fact]
    public void Below_NoRoomUnder_FlipsAbove() =>
        Assert.Equal((150, 492), PopupPlacement.Below(new RectI(100, 800, 400, 1000), Main, 200, 300, 8));

    [Fact]
    public void Below_ClampedToTheScreen()
    {
        Assert.Equal((0, 318), PopupPlacement.Below(new RectI(-50, 100, 50, 310), Main, 200, 300, 8));
        Assert.Equal((1720, 318), PopupPlacement.Below(new RectI(1850, 100, 1950, 310), Main, 200, 300, 8));
        // Taller than the room either side: kept on screen, top-aligned.
        Assert.Equal((150, 0), PopupPlacement.Below(new RectI(100, 400, 400, 600), Main, 200, 1200, 8));
    }
}
