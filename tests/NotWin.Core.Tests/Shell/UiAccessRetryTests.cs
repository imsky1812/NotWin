using NotWin.Core.Shell;

namespace NotWin.Core.Tests.Shell;

/// <summary>
/// User 2026-11-27: after a reboot NotWin's start-with-Windows copy came up without uiAccess (twice that day) and looked
/// closed. The installed copy then relaunches itself through Explorer, a couple of times at most.
/// </summary>
public sealed class UiAccessRetryTests
{
    [Fact]
    public void InstalledCopy_WithoutUiAccess_Relaunches() =>
        Assert.True(UiAccessRetry.ShouldRelaunch(uiAccess: false, installedCopy: true, attempt: 0));

    [Fact]
    public void WithUiAccess_NeverRelaunches() =>
        Assert.False(UiAccessRetry.ShouldRelaunch(uiAccess: true, installedCopy: true, attempt: 0));

    [Fact]
    public void DevCopy_NeverRelaunches() =>
        Assert.False(UiAccessRetry.ShouldRelaunch(uiAccess: false, installedCopy: false, attempt: 0)); // dotnet run, out\app

    [Fact]
    public void GivesUp_AfterTwoTries()
    {
        Assert.True(UiAccessRetry.ShouldRelaunch(false, true, attempt: 1));
        Assert.False(UiAccessRetry.ShouldRelaunch(false, true, attempt: 2));
    }

    [Fact]
    public void NextArgs_WaitForThisOne_AndCountTheTry()
    {
        var next = UiAccessRetry.Next(pid: 77, attempt: 0);
        Assert.Equal(77, RestartArgs.WaitFor(next));
        Assert.Equal(1, UiAccessRetry.Attempt(next));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("--uiaccess-try 2", 2)]
    [InlineData("--after 5 --uiaccess-try 1", 1)]
    [InlineData("--uiaccess-try x", 0)]
    [InlineData("--uiaccess-try", 0)]
    public void Attempt_ReadsTheCount(string line, int expected) =>
        Assert.Equal(expected, UiAccessRetry.Attempt(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    [Fact]
    public void Delay_GrowsButStaysShort()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), UiAccessRetry.Delay(0));
        Assert.Equal(TimeSpan.FromSeconds(10), UiAccessRetry.Delay(1));
    }
}
