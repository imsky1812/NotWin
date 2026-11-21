using NothingShell.Core.Timing;

namespace NothingShell.Core.Tests.Timing;

public sealed class TimerModelTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    private static TimeSpan M(double m) => TimeSpan.FromMinutes(m);

    [Fact]
    public void Default_25Minutes_Idle()
    {
        var t = new TimerModel(25);
        Assert.Equal(TimerPhase.Idle, t.Phase);
        Assert.Equal(M(25), t.Remaining(S(0)));
        Assert.Equal(1, t.Fraction(S(0)));
    }

    [Fact]
    public void Minus_NotBelowOneMinute()
    {
        var t = new TimerModel(2);
        t.Minus(S(0)); t.Minus(S(0)); t.Minus(S(0));
        Assert.Equal(M(1), t.Remaining(S(0)));
        Assert.Equal(1, t.Minutes);
    }

    [Fact]
    public void Plus_Idle_CappedAt99()
    {
        var t = new TimerModel(98);
        t.Plus(S(0)); t.Plus(S(0));
        Assert.Equal(M(99), t.Duration);
    }

    [Fact]
    public void Toggle_RunsFromNow()
    {
        var t = new TimerModel(25);
        t.Toggle(S(0));
        Assert.Equal(TimerPhase.Running, t.Phase);
        Assert.Equal(M(25) - S(10), t.Remaining(S(10)));
    }

    [Fact]
    public void Pause_FreezesRemaining()
    {
        var t = new TimerModel(25);
        t.Toggle(S(0));
        t.Toggle(S(10));
        Assert.Equal(TimerPhase.Paused, t.Phase);
        Assert.Equal(M(25) - S(10), t.Remaining(S(100)));
        t.Toggle(S(100));
        Assert.Equal(M(25) - S(20), t.Remaining(S(110)));
    }

    [Fact]
    public void Plus_WhileRunning_AddsToRemaining()
    {
        var t = new TimerModel(1);
        t.Toggle(S(0));
        t.Plus(S(30));
        Assert.Equal(S(90), t.Remaining(S(30)));
        Assert.True(t.Fraction(S(30)) <= 1);
    }

    [Fact]
    public void Minus_WhileRunning_UnderAMinute_DoesNothing()
    {
        var t = new TimerModel(1);
        t.Toggle(S(0));
        t.Minus(S(30));
        Assert.Equal(S(30), t.Remaining(S(30)));
    }

    [Fact]
    public void Tick_FinishesOnce()
    {
        var t = new TimerModel(1);
        t.Toggle(S(0));
        Assert.False(t.Tick(S(59)));
        Assert.True(t.Tick(S(60)));
        Assert.False(t.Tick(S(61)));
        Assert.Equal(TimerPhase.Finished, t.Phase);
    }

    [Fact]
    public void Tick_AfterLongGap_FinishesOnce()
    {
        var t = new TimerModel(25);
        t.Toggle(S(0));
        Assert.True(t.Tick(TimeSpan.FromHours(10)));
        Assert.False(t.Tick(TimeSpan.FromHours(11)));
        Assert.Equal(TimeSpan.Zero, t.Remaining(TimeSpan.FromHours(11)));
    }

    [Fact]
    public void Remaining_NeverNegative()
    {
        var t = new TimerModel(1);
        t.Toggle(S(0));
        Assert.Equal(TimeSpan.Zero, t.Remaining(S(500)));
        Assert.Equal(0, t.Fraction(S(500)));
    }

    [Fact]
    public void Toggle_AfterFinish_ResetsToDuration()
    {
        var t = new TimerModel(1);
        t.Toggle(S(0));
        t.Tick(S(60));
        t.Toggle(S(70));
        Assert.Equal(TimerPhase.Idle, t.Phase);
        Assert.Equal(M(1), t.Remaining(S(70)));
    }
}
