using NotWin.Core.Widgets;

namespace NotWin.Core.Tests.Widgets;

public sealed class ScreenTimeLogTests
{
    private static readonly DateTime Day = new(2026, 11, 26, 10, 0, 0);
    private static readonly TimeSpan Busy = TimeSpan.FromSeconds(10);

    [Fact]
    public void Sample_CountsActiveMinutes()
    {
        var log = new ScreenTimeLog(null);
        Assert.False(log.Sample(Day, true, Busy)); // baseline only
        Assert.True(log.Sample(Day.AddMinutes(1), true, Busy));
        log.Sample(Day.AddMinutes(2), true, Busy);
        Assert.Equal(2, log.Today(Day.AddMinutes(2)));
    }

    [Fact]
    public void Sample_LockedOrIdle_AddsNothing()
    {
        var log = new ScreenTimeLog(null);
        log.Sample(Day, true, Busy);
        Assert.False(log.Sample(Day.AddMinutes(1), false, Busy));
        Assert.False(log.Sample(Day.AddMinutes(2), true, TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1))));
        Assert.Equal(0, log.Today(Day.AddMinutes(2)));
    }

    [Fact]
    public void Sample_AfterGap_AddsNothing()
    {
        var log = new ScreenTimeLog(null);
        log.Sample(Day, true, Busy);
        log.Sample(Day.AddMinutes(20), true, Busy);
        Assert.Equal(0, log.Today(Day.AddMinutes(20)));
    }

    [Fact]
    public void Sample_CarriesSeconds()
    {
        var log = new ScreenTimeLog(null);
        var t = Day;
        log.Sample(t, true, Busy);
        for (var i = 0; i < 4; i++) log.Sample(t = t.AddSeconds(45), true, Busy); // 3 minutes in 45 s steps
        Assert.Equal(3, log.Today(t));
    }

    [Fact]
    public void Sample_AtMidnight_GoesToNewDay()
    {
        var log = new ScreenTimeLog(new Dictionary<string, int> { ["2026-11-26"] = 30 });
        var late = new DateTime(2026, 11, 26, 23, 59, 30);
        log.Sample(late, true, Busy);
        log.Sample(late.AddMinutes(1), true, Busy);
        Assert.Equal(1, log.Today(late.AddMinutes(1)));
        Assert.Equal(30, log.Today(late));
    }

    [Fact]
    public void Week_FillsMissingDaysWithZero()
    {
        var log = new ScreenTimeLog(new Dictionary<string, int> { ["2026-11-20"] = 60, ["2026-11-26"] = 5, ["2026-11-01"] = 99 });
        var week = log.Week(Day);
        Assert.Equal(7, week.Count);
        Assert.Equal(new DateOnly(2026, 11, 20), week[0].Day);
        Assert.Equal(60, week[0].Minutes);
        Assert.Equal(0, week[1].Minutes);
        Assert.Equal((new DateOnly(2026, 11, 26), 5), week[6]);
    }

    [Fact]
    public void Average7_IgnoresTodayAndEmptyDays()
    {
        var log = new ScreenTimeLog(new Dictionary<string, int> { ["2026-11-24"] = 100, ["2026-11-25"] = 200, ["2026-11-26"] = 999 });
        Assert.Equal(150, log.Average7(Day));
        Assert.Equal(0, new ScreenTimeLog(null).Average7(Day));
    }

    [Fact]
    public void ToSave_KeepsEight()
    {
        var saved = Enumerable.Range(0, 12).ToDictionary(i => new DateOnly(2026, 11, 26).AddDays(-i).ToString("yyyy-MM-dd"), i => i + 1);
        var kept = new ScreenTimeLog(saved).ToSave(Day);
        Assert.Equal(8, kept.Count);
        Assert.True(kept.ContainsKey("2026-11-19"));
        Assert.False(kept.ContainsKey("2026-11-18"));
    }

    [Fact]
    public void Ignores_BadSavedKeys()
    {
        var log = new ScreenTimeLog(new Dictionary<string, int> { ["nonsense"] = 5, ["2026-11-26"] = -4 });
        Assert.Equal(0, log.Today(Day));
    }

    [Fact]
    public void Format_Examples()
    {
        Assert.Equal("6H20", ScreenTimeLog.Format(380));
        Assert.Equal("0H05", ScreenTimeLog.Format(5));
        Assert.Equal("12H00", ScreenTimeLog.Format(720));
    }
}
