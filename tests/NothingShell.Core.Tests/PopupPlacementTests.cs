namespace NothingShell.Core.Tests;

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
}
