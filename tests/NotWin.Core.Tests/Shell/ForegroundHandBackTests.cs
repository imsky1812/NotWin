using NotWin.Core.Shell;

namespace NotWin.Core.Tests.Shell;

public sealed class ForegroundHandBackTests
{
    private const nint Notch = 100, Editor = 200;

    [Fact]
    public void NotchStillForeground_GivesItBackToThePreviousWindow() =>
        Assert.Equal(Editor, ForegroundHandBack.Target(previous: Editor, current: Notch, own: Notch, isWindow: _ => true));

    [Fact]
    public void UserAlreadyClickedElsewhere_LeavesItAlone() =>
        Assert.Null(ForegroundHandBack.Target(previous: Editor, current: 300, own: Notch, isWindow: _ => true));

    [Fact]
    public void PreviousWindowGone_Null() =>
        Assert.Null(ForegroundHandBack.Target(previous: Editor, current: Notch, own: Notch, isWindow: _ => false));

    [Fact]
    public void NoPreviousOrItWasTheNotch_Null()
    {
        Assert.Null(ForegroundHandBack.Target(previous: 0, current: Notch, own: Notch, isWindow: _ => true));
        Assert.Null(ForegroundHandBack.Target(previous: Notch, current: Notch, own: Notch, isWindow: _ => true));
    }
}
