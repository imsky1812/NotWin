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

    // Final review M4: a birthday / holiday / trip in progress must not hide today's meetings on the 2×1 widget.
    [Fact]
    public void NextEvent_AllDayInProgress_GivesWayToTodaysTimedEvent()
    {
        var now = At(11, 21, 10);
        var birthday = AllDay(At(11, 21), At(11, 22), "birthday");
        var trip = AllDay(At(11, 19), At(11, 26), "trip");
        var meeting = Timed(At(11, 21, 14), At(11, 21, 15), "meeting");
        var tomorrow = Timed(At(11, 22, 9), At(11, 22, 10), "tomorrow");
        Assert.Equal("meeting", CalendarFeed.NextEvent([birthday, trip, meeting, tomorrow], now)?.Title);
        // Nothing timed left today: the all-day event shows ("TODAY").
        Assert.Equal("birthday", CalendarFeed.NextEvent([birthday, tomorrow], now)?.Title);
        // An all-day event that starts later (tomorrow) never beats an earlier timed one.
        Assert.Equal("meeting", CalendarFeed.NextEvent([AllDay(At(11, 22), At(11, 23)), meeting], now)?.Title);
    }

    // Final review I2 / M3: what a Google API error is about.
    [Fact]
    public void ErrorReason_ReadsLegacyAndDetailsReasons()
    {
        Assert.Equal("accessNotConfigured", CalendarFeed.ErrorReason("""
            {"error":{"code":403,"message":"Google Calendar API has not been used in project 1 before or it is disabled.",
             "errors":[{"message":"x","domain":"usageLimits","reason":"accessNotConfigured"}],"status":"PERMISSION_DENIED",
             "details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"SERVICE_DISABLED"}]}}
            """));
        Assert.Equal("SERVICE_DISABLED", CalendarFeed.ErrorReason("""
            {"error":{"code":403,"status":"PERMISSION_DENIED","details":[{"reason":"SERVICE_DISABLED"}]}}
            """));
        Assert.Equal("rateLimitExceeded", CalendarFeed.ErrorReason("""{"error":{"errors":[{"reason":"rateLimitExceeded"}]}}"""));
        Assert.Null(CalendarFeed.ErrorReason("garbage"));
        Assert.Null(CalendarFeed.ErrorReason("""{"error":"x"}"""));
    }

    [Theory]
    [InlineData("accessNotConfigured", true, false)]
    [InlineData("SERVICE_DISABLED", true, false)]
    [InlineData("rateLimitExceeded", false, true)]
    [InlineData("userRateLimitExceeded", false, true)]
    [InlineData("quotaExceeded", false, true)]
    [InlineData("forbidden", false, false)]
    [InlineData(null, false, false)]
    public void ErrorReason_Kinds(string? reason, bool disabled, bool rateLimited)
    {
        Assert.Equal(disabled, CalendarFeed.IsApiDisabled(reason));
        Assert.Equal(rateLimited, CalendarFeed.IsRateLimited(reason));
    }

    // Plan 6b: the "Up next" widget shows only the user's own events.
    [Fact]
    public void ParseEvents_MarksHolidaysBirthdaysAndWorkingLocationNotPersonal()
    {
        const string json = """
            {"items":[
              {"summary":"Flight to Goa","eventType":"fromGmail","location":"DEL T3","htmlLink":"https://www.google.com/calendar/event?eid=abc",
               "start":{"dateTime":"2026-11-24T06:00:00Z"},"end":{"dateTime":"2026-11-24T08:30:00Z"}},
              {"summary":"Mum","eventType":"birthday","start":{"date":"2026-11-25"},"end":{"date":"2026-11-26"}},
              {"summary":"Office","eventType":"workingLocation","start":{"date":"2026-11-25"},"end":{"date":"2026-11-26"}},
              {"summary":"Standup","eventType":"default","start":{"dateTime":"2026-11-25T09:00:00Z"},"end":{"dateTime":"2026-11-25T09:15:00Z"}}
            ]}
            """;
        var mine = CalendarFeed.ParseEvents(json, Utc, "me@gmail.com").Events;
        Assert.Equal([true, false, false, true], mine.Select(e => e.Personal));
        Assert.Equal("DEL T3", mine[0].Location);
        Assert.Equal("https://www.google.com/calendar/event?eid=abc", mine[0].Link);
        Assert.Null(mine[3].Location);
        var holidays = CalendarFeed.ParseEvents(json, Utc, "en.indian#holiday@group.v.calendar.google.com").Events;
        Assert.All(holidays, e => Assert.False(e.Personal));
        Assert.All(CalendarFeed.ParseEvents(json, Utc, "addressbook#contacts@group.v.calendar.google.com").Events, e => Assert.False(e.Personal));
    }

    [Fact]
    public void EventsUrl_AsksForTypePlaceAndLink()
    {
        var url = Uri.UnescapeDataString(CalendarFeed.EventsUrl("a", At(11, 1), At(12, 1), null));
        Assert.Contains("items(summary,status,start,end,eventType,location,htmlLink,attendees(self,responseStatus))", url);
    }

    [Fact]
    public void Upcoming_PersonalNotEnded_InOrder_Capped()
    {
        var now = At(11, 21, 10);
        var events = new[]
        {
            Timed(At(11, 21, 15), At(11, 21, 16), "c"),
            Timed(At(11, 21, 8), At(11, 21, 9), "ended"),
            Timed(At(11, 21, 9, 30), At(11, 21, 11), "b"),
            new CalEvent("holiday", At(11, 21), At(11, 22), true, Personal: false),
            Timed(At(11, 22, 9), At(11, 22, 10), "d"),
            Timed(At(11, 23, 9), At(11, 23, 10), "e"),
        };
        Assert.Equal(["b", "c", "d"], CalendarFeed.Upcoming(events, now, 3).Select(e => e.Title));
        Assert.Empty(CalendarFeed.Upcoming([], now, 4));
    }

    [Fact]
    public void Countdown_SoonThenWhenLabel()
    {
        var now = At(11, 21, 10);
        Assert.Equal(("NOW", true), CalendarFeed.Countdown(Timed(At(11, 21, 9), At(11, 21, 11)), now, Utc));
        Assert.Equal(("IN 25 MIN", true), CalendarFeed.Countdown(Timed(At(11, 21, 10, 25), At(11, 21, 11)), now, Utc));
        Assert.Equal(("IN 1 MIN", true), CalendarFeed.Countdown(Timed(At(11, 21, 10, 0).AddSeconds(20), At(11, 21, 11)), now, Utc));
        Assert.Equal(("IN 3 H", false), CalendarFeed.Countdown(Timed(At(11, 21, 13, 10), At(11, 21, 14)), now, Utc));
        Assert.Equal(("TODAY", false), CalendarFeed.Countdown(AllDay(At(11, 21), At(11, 22)), now, Utc));
        Assert.Equal(("TOMORROW 09:00", false), CalendarFeed.Countdown(Timed(At(11, 22, 9), At(11, 22, 10)), now, Utc));
        Assert.Equal(("TUE 24", false), CalendarFeed.Countdown(Timed(At(11, 24, 9), At(11, 24, 10)), now, Utc));
    }

    [Theory]
    [InlineData("https://www.google.com/calendar/event?eid=abc", true)]
    [InlineData("https://calendar.google.com/calendar/event?eid=abc", true)]
    [InlineData("http://www.google.com/calendar/event?eid=abc", false)]
    [InlineData("https://evil.example/calendar", false)]
    [InlineData("file:///c:/windows", false)]
    [InlineData(null, false)]
    public void IsSafeLink_OnlyGoogleCalendarHttps(string? link, bool safe) => Assert.Equal(safe, CalendarFeed.IsSafeLink(link));

    // Review (Plan 6b) #1: a running all-day plan (a trip) must not hide today's meeting at the 2×1 size.
    [Fact]
    public void Upcoming_TodaysTimedEventsBeforeARunningAllDayPlan()
    {
        var now = At(11, 21, 10);
        var trip = AllDay(At(11, 19), At(11, 26), "trip");
        var meeting = Timed(At(11, 21, 10, 20), At(11, 21, 11), "meeting");
        var tomorrowAllDay = AllDay(At(11, 22), At(11, 23), "tomorrow all day");
        var tomorrowTimed = Timed(At(11, 22, 9), At(11, 22, 10), "tomorrow 9");
        Assert.Equal("meeting", CalendarFeed.Upcoming([trip, meeting, tomorrowAllDay, tomorrowTimed], now, 1).Single().Title);
        Assert.Equal(["meeting", "trip", "tomorrow all day", "tomorrow 9"],
            CalendarFeed.Upcoming([tomorrowTimed, trip, tomorrowAllDay, meeting], now, 9).Select(e => e.Title));
    }

    // Review (Plan 6b) #2: every calendar Google fills for everyone (holidays, week numbers, sports...), declined
    // invites and calendars the user can only read are not the user's plans.
    [Theory]
    [InlineData("en.indian#holiday@group.v.calendar.google.com", true)]
    [InlineData("e_2_en#weeknum@group.v.calendar.google.com", true)]
    [InlineData("ht3jlfaac5lfd6263ulfh4tql8@group.v.calendar.google.com", true)]
    [InlineData("addressbook#contacts@group.v.calendar.google.com", true)]
    [InlineData("abc123@group.calendar.google.com", false)]
    [InlineData("someone@gmail.com", false)]
    [InlineData(null, false)]
    public void IsSharedFeed_AnyGoogleGeneratedCalendar(string? id, bool shared) => Assert.Equal(shared, CalendarFeed.IsSharedFeed(id));

    [Fact]
    public void ParseEvents_DeclinedInvite_IsNotPersonal_ReadOnlyCalendar_Neither()
    {
        const string json = """
            {"items":[
              {"summary":"Declined","attendees":[{"self":true,"responseStatus":"declined"},{"responseStatus":"accepted"}],
               "start":{"dateTime":"2026-11-24T06:00:00Z"},"end":{"dateTime":"2026-11-24T07:00:00Z"}},
              {"summary":"Accepted","attendees":[{"self":true,"responseStatus":"accepted"}],
               "start":{"dateTime":"2026-11-24T08:00:00Z"},"end":{"dateTime":"2026-11-24T09:00:00Z"}}
            ]}
            """;
        Assert.Equal([false, true], CalendarFeed.ParseEvents(json, Utc, "me@gmail.com").Events.Select(e => e.Personal));
        Assert.All(CalendarFeed.ParseEvents(json, Utc, "boss@company.com", mine: false).Events, e => Assert.False(e.Personal));
        Assert.Contains("attendees(self,responseStatus)", Uri.UnescapeDataString(CalendarFeed.EventsUrl("a", At(11, 1), At(12, 1), null)));
    }

    [Fact]
    public void ReadOnlyCalendars_ReaderAndFreeBusy()
    {
        const string json = """
            {"items":[{"id":"me@gmail.com","selected":true,"accessRole":"owner"},
                      {"id":"boss@company.com","selected":true,"accessRole":"reader"},
                      {"id":"busy@company.com","selected":true,"accessRole":"freeBusyReader"},
                      {"id":"team@group.calendar.google.com","selected":true,"accessRole":"writer"}]}
            """;
        Assert.Equal(["boss@company.com", "busy@company.com"], CalendarFeed.ReadOnlyCalendars(json).Order());
        Assert.Contains("accessRole", Uri.UnescapeDataString(CalendarFeed.CalendarListUrl(null)));
        Assert.Empty(CalendarFeed.ReadOnlyCalendars("garbage"));
    }

    // Review (Plan 6b) #5: no "IN 60 MIN"; hours round to the nearest.
    [Fact]
    public void Countdown_Edges()
    {
        var now = At(11, 21, 10);
        Assert.Equal(("IN 59 MIN", true), CalendarFeed.Countdown(Timed(now.AddMinutes(59).AddSeconds(30), now.AddHours(2)), now, Utc));
        Assert.Equal(("IN 2 H", false), CalendarFeed.Countdown(Timed(now.AddMinutes(119), now.AddHours(3)), now, Utc));
    }
}
