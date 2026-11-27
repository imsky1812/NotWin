using NotWin.Core.Shell;

namespace NotWin.Core.Tests.Shell;

/// <summary>
/// User 2026-11-27: "nothing shell isn't working" — Shut down was chosen, Windows asked NotWin to end, NotWin gave the
/// taskbar back, then the shutdown never happened and NotWin stayed half-alive. A cancelled shutdown now resumes it.
/// </summary>
public sealed class SessionEndTrackerTests
{
    [Fact]
    public void Query_GivesTheTaskbarBack()
    {
        var t = new SessionEndTracker();
        Assert.Equal(SessionEndAction.GiveBack, t.Query());
    }

    [Fact]
    public void CancelledAfterQuery_Resumes()
    {
        var t = new SessionEndTracker();
        t.Query();
        Assert.Equal(SessionEndAction.Resume, t.Ended(ending: false));
    }

    [Fact]
    public void RealEnd_DoesNothingMore()
    {
        var t = new SessionEndTracker();
        t.Query();
        Assert.Equal(SessionEndAction.None, t.Ended(ending: true));
    }

    [Fact]
    public void CancelWithoutQuery_DoesNothing()
    {
        // WM_ENDSESSION(FALSE) without our own query (another app's veto round we never saw) changes nothing.
        Assert.Equal(SessionEndAction.None, new SessionEndTracker().Ended(ending: false));
    }

    [Fact]
    public void ResumesOnlyOnce_PerQuery()
    {
        var t = new SessionEndTracker();
        t.Query();
        t.Ended(ending: false);
        Assert.Equal(SessionEndAction.None, t.Ended(ending: false));
        t.Query();
        Assert.Equal(SessionEndAction.Resume, t.Ended(ending: false));
    }

    [Fact]
    public void RestartArgs_RoundTrip()
    {
        Assert.Equal(["--after", "4242"], RestartArgs.For(4242));
        Assert.Equal(4242, RestartArgs.WaitFor(["--after", "4242"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--after")]
    [InlineData("--after abc")]
    [InlineData("--after -5")]
    [InlineData("--quit")]
    public void RestartArgs_NoneOrBad_IsNull(string line) =>
        Assert.Null(RestartArgs.WaitFor(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
}
