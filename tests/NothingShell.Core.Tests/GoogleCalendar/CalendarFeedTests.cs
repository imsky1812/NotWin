using NothingShell.Core.GoogleCalendar;

namespace NothingShell.Core.Tests.GoogleCalendar;

public sealed class CalendarFeedTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static DateTimeOffset At(int m, int d, int h = 0, int min = 0) => new(2026, m, d, h, min, 0, TimeSpan.Zero);
    private static CalEvent Timed(DateTimeOffset s, DateTimeOffset e, string t = "x") => new(t, s, e, false);
    private static CalEvent AllDay(DateTimeOffset s, DateTimeOffset e, string t = "x") => new(t, s, e, true);

    [Fact]
    public void ParseCalendarList_KeepsSelectedOnly()
    {
        var (ids, next) = CalendarFeed.ParseCalendarList("""
            {"items":[{"id":"a","selected":true},{"id":"b"},{"id":"c","selected":true,"deleted":true},{"selected":true}]}
            """);
        Assert.Equal(["a"], ids);
        Assert.Null(next);
    }

    [Fact]
    public void ParseCalendarList_ReadsNextPageToken() =>
        Assert.Equal("p2", CalendarFeed.ParseCalendarList("""{"items":[],"nextPageToken":"p2"}""").Next);

    [Fact]
    public void ParseEvents_TimedAndAllDay()
    {
        var (events, next) = CalendarFeed.ParseEvents("""
            {"items":[
              {"summary":"Standup","status":"confirmed","start":{"dateTime":"2026-11-21T09:30:00+05:30"},"end":{"dateTime":"2026-11-21T10:00:00+05:30"}},
              {"summary":"Holiday","start":{"date":"2026-11-25"},"end":{"date":"2026-11-26"}},
              {"start":{"date":"2026-11-27"},"end":{"date":"2026-11-28"}}
            ],"nextPageToken":"n"}
            """, Utc);
        Assert.Equal("n", next);
        Assert.Equal(3, events.Count);
        Assert.Equal(new CalEvent("Standup", new DateTimeOffset(2026, 11, 21, 9, 30, 0, TimeSpan.FromHours(5.5)),
            new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(5.5)), false), events[0]);
        Assert.Equal(AllDay(At(11, 25), At(11, 26), "Holiday"), events[1]);
        Assert.Equal("(No title)", events[2].Title);
    }

    [Fact]
    public void ParseEvents_AllDay_UsesTheLocalZone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST");
        var e = CalendarFeed.ParseEvents("""{"items":[{"summary":"H","start":{"date":"2026-11-25"},"end":{"date":"2026-11-26"}}]}""", zone).Events[0];
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.FromHours(5.5)), e.Start);
    }

    [Fact]
    public void ParseEvents_SkipsCancelled()
    {
        var (events, _) = CalendarFeed.ParseEvents("""
            {"items":[{"summary":"Gone","status":"cancelled","start":{"date":"2026-11-25"},"end":{"date":"2026-11-26"}},
                      {"summary":"No start","end":{"date":"2026-11-26"}}]}
            """, Utc);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{}")]
    [InlineData("""{"items":"nope"}""")]
    [InlineData("""{"items":[{"start":{"dateTime":"not a date"},"end":{"dateTime":"x"}}]}""")]
    public void ParseEvents_Garbage_IsEmpty(string json)
    {
        Assert.Empty(CalendarFeed.ParseEvents(json, Utc).Events);
        Assert.Empty(CalendarFeed.ParseCalendarList(json).Ids);
    }

    [Fact]
    public void DaysWithEvents_AllDayEndIsExclusive() =>
        Assert.Equal([new DateOnly(2026, 11, 30)], CalendarFeed.DaysWithEvents([AllDay(At(11, 30), At(12, 1))], Utc));

    [Fact]
    public void DaysWithEvents_MultiDayAllDay() =>
        Assert.Equal(3, CalendarFeed.DaysWithEvents([AllDay(At(11, 29), At(12, 2))], Utc).Count);

    [Fact]
    public void DaysWithEvents_TimedAcrossMidnight() =>
        Assert.Equal([new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 22)],
            CalendarFeed.DaysWithEvents([Timed(At(11, 21, 23), At(11, 22, 1))], Utc).Order());

    [Fact]
    public void DaysWithEvents_TimedEndingAtMidnight() =>
        Assert.Equal([new DateOnly(2026, 11, 21)], CalendarFeed.DaysWithEvents([Timed(At(11, 21, 22), At(11, 22))], Utc));

    [Fact]
    public void DaysWithEvents_ZeroLengthEvent_StillMarksItsDay() =>
        Assert.Equal([new DateOnly(2026, 11, 21)], CalendarFeed.DaysWithEvents([Timed(At(11, 21, 9), At(11, 21, 9))], Utc));

    [Fact]
    public void NextEvent_SkipsEnded_IncludesInProgress()
    {
        var now = At(11, 21, 10);
        var ended = Timed(At(11, 21, 8), At(11, 21, 9), "ended");
        var running = Timed(At(11, 21, 9, 30), At(11, 21, 11), "running");
        var later = Timed(At(11, 21, 15), At(11, 21, 16), "later");
        Assert.Equal("running", CalendarFeed.NextEvent([later, ended, running], now)?.Title);
        Assert.Equal("later", CalendarFeed.NextEvent([later, ended], now)?.Title);
        Assert.Null(CalendarFeed.NextEvent([ended], now));
        Assert.Null(CalendarFeed.NextEvent([], now));
    }

    [Fact]
    public void WhenLabel_TodayTomorrowLater()
    {
        var now = At(11, 21, 10); // a Saturday
        Assert.Equal("TODAY 14:30", CalendarFeed.WhenLabel(Timed(At(11, 21, 14, 30), At(11, 21, 15)), now, Utc));
        Assert.Equal("NOW", CalendarFeed.WhenLabel(Timed(At(11, 21, 9), At(11, 21, 11)), now, Utc));
        Assert.Equal("TODAY", CalendarFeed.WhenLabel(AllDay(At(11, 21), At(11, 22)), now, Utc));
        Assert.Equal("TOMORROW 09:00", CalendarFeed.WhenLabel(Timed(At(11, 22, 9), At(11, 22, 10)), now, Utc));
        Assert.Equal("TOMORROW", CalendarFeed.WhenLabel(AllDay(At(11, 22), At(11, 23)), now, Utc));
        Assert.Equal("TUE 24", CalendarFeed.WhenLabel(Timed(At(11, 24, 9), At(11, 24, 10)), now, Utc));
    }

    [Fact]
    public void Window_FromWeekBeforeMonthStart_To60DaysAhead()
    {
        var now = At(11, 21, 10);
        var (from, to) = CalendarFeed.Window(now);
        Assert.Equal(At(10, 25), from);
        Assert.Equal(now.AddDays(60), to);
    }

    [Fact]
    public void EventsUrl_EscapesCalendarId()
    {
        var url = CalendarFeed.EventsUrl("a#b@group.calendar.google.com", At(11, 1), At(12, 1), "pg");
        Assert.StartsWith("https://www.googleapis.com/calendar/v3/calendars/a%23b%40group.calendar.google.com/events?", url);
        Assert.Contains("singleEvents=true", url);
        Assert.Contains("orderBy=startTime", url);
        Assert.Contains("maxResults=250", url);
        Assert.Contains("timeMin=" + Uri.EscapeDataString("2026-11-01T00:00:00Z"), url);
        Assert.Contains("pageToken=pg", url);
        Assert.Contains("fields=", url);
        Assert.DoesNotContain("pageToken", CalendarFeed.EventsUrl("a", At(11, 1), At(12, 1), null));
    }

    [Fact]
    public void CalendarListUrl_AsksForIdsAndSelection()
    {
        var url = CalendarFeed.CalendarListUrl(null);
        Assert.StartsWith("https://www.googleapis.com/calendar/v3/users/me/calendarList?", url);
        Assert.Contains("fields=", url);
        Assert.Contains("pageToken=x", CalendarFeed.CalendarListUrl("x"));
    }
}
