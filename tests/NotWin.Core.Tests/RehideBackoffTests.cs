using NotWin.Core;

namespace NotWin.Core.Tests;

public sealed class RehideBackoffTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void TwentyRehidesWithinTwoSeconds_AreAllowed()
    {
        var b = new RehideBackoff();
        for (var i = 0; i < 20; i++) Assert.Equal(RehideDecision.Proceed, b.Next(Ms(i * 50)));
    }

    [Fact]
    public void TwentyFirst_StartsPauseOnce_ThenStaysPaused()
    {
        var b = new RehideBackoff();
        for (var i = 0; i < 20; i++) b.Next(Ms(i * 50));
        Assert.Equal(RehideDecision.PauseStarted, b.Next(Ms(1000)));
        Assert.Equal(RehideDecision.Paused, b.Next(Ms(1100)));
        Assert.Equal(RehideDecision.Paused, b.Next(Ms(5999)));
    }

    [Fact]
    public void AfterPause_NextCheckProceeds()
    {
        var b = new RehideBackoff();
        for (var i = 0; i < 20; i++) b.Next(Ms(i * 50));
        b.Next(Ms(1000));
        Assert.Equal(RehideDecision.Proceed, b.Next(Ms(6000)));
    }

    [Fact]
    public void SlowRehides_NeverPause()
    {
        var b = new RehideBackoff();
        for (var i = 0; i < 100; i++) Assert.Equal(RehideDecision.Proceed, b.Next(Ms(i * 500)));
    }
}
