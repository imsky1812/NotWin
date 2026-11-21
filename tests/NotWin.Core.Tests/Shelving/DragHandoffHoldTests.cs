using NotWin.Core.Shelving;

namespace NotWin.Core.Tests.Shelving;

// User report 2026-11-21 (draghelper.log "no press reached the grip"): the notch stays click-through while the helper
// takes the drag over, and becomes clickable again once the button is really up.
public sealed class DragHandoffHoldTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void KeepsHolding_WhileTheButtonIsDown() =>
        Assert.False(DragHandoffHold.ShouldRestore(Ms(5000), releasedPolls: 0));

    [Fact]
    public void IgnoresTheHelpersOwnQuickRelease() =>
        // The helper re-sends a release then a press: one "up" poll early on is not the user letting go.
        Assert.False(DragHandoffHold.ShouldRestore(Ms(300), releasedPolls: 1));

    [Fact]
    public void Restores_OnceReleasedTwiceAfterASecond()
    {
        Assert.False(DragHandoffHold.ShouldRestore(Ms(900), releasedPolls: 5));
        Assert.True(DragHandoffHold.ShouldRestore(Ms(1000), releasedPolls: 2));
    }

    [Fact]
    public void Restores_AfterAMinuteWhatever() => Assert.True(DragHandoffHold.ShouldRestore(Ms(60_000), releasedPolls: 0));
}
