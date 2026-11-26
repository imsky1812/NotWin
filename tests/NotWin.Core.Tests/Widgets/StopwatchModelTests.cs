using NotWin.Core.Widgets;

namespace NotWin.Core.Tests.Widgets;

public sealed class StopwatchModelTests
{
    private TimeSpan _now = TimeSpan.FromHours(5);
    private StopwatchModel Make() => new(() => _now);
    private void Advance(double s) => _now += TimeSpan.FromSeconds(s);

    [Fact]
    public void Starts_Pauses_Resumes()
    {
        var w = Make();
        Assert.False(w.Running);
        w.Start();
        Advance(1.5);
        w.Pause();
        Advance(10);
        Assert.Equal(TimeSpan.FromSeconds(1.5), w.Elapsed);
        w.Start();
        Advance(1);
        Assert.True(w.Running);
        Assert.Equal(TimeSpan.FromSeconds(2.5), w.Elapsed);
    }

    [Fact]
    public void Reset_OnlyWhenPaused()
    {
        var w = Make();
        w.Start();
        Advance(3);
        w.Reset();
        Assert.Equal(TimeSpan.FromSeconds(3), w.Elapsed);
        w.Pause();
        w.Reset();
        Assert.Equal(TimeSpan.Zero, w.Elapsed);
        Assert.False(w.Running);
    }

    [Fact]
    public void UntilNextSecond()
    {
        var w = Make();
        w.Start();
        Advance(2.25);
        Assert.Equal(TimeSpan.FromSeconds(0.75), w.UntilNextSecond);
    }

    [Fact]
    public void Format()
    {
        Assert.Equal("00:00", StopwatchModel.Format(TimeSpan.Zero));
        Assert.Equal("01:15", StopwatchModel.Format(TimeSpan.FromSeconds(75.9)));
        Assert.Equal("1:02", StopwatchModel.Format(TimeSpan.FromSeconds(3725)));
    }
}
