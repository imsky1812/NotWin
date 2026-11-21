using System.Globalization;
using System.Text.Json;

namespace NothingShell.Core.GoogleCalendar;

/// <summary>
/// One calendar event. All-day events start and end at local midnight of their <c>date</c> and their exclusive end
/// <c>date</c>. The title is private: never logged.
/// </summary>
public sealed record CalEvent(string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay);

/// <summary>
/// The Google Calendar API v3 as the Calendar widget uses it (Plan 6): every calendar switched on in Google Calendar
/// (<c>selected</c>, user 2026-11-21), their events from a week before the month to 60 days ahead, the days that get a
/// dot, and the next event with its "TODAY 14:30" label. Pure rules; bad responses give empty results, never throw.
/// </summary>
public static class CalendarFeed
{
    private const string Api = "https://www.googleapis.com/calendar/v3/";

    public static string CalendarListUrl(string? pageToken) =>
        Api + "users/me/calendarList?" + GoogleOAuth.Query(Pairs([("fields", "items(id,selected,deleted),nextPageToken")], pageToken));

    public static string EventsUrl(string calendarId, DateTimeOffset from, DateTimeOffset to, string? pageToken) =>
        Api + "calendars/" + Uri.EscapeDataString(calendarId) + "/events?" + GoogleOAuth.Query(Pairs(
        [
            ("timeMin", Rfc3339(from)), ("timeMax", Rfc3339(to)), ("singleEvents", "true"), ("orderBy", "startTime"),
            ("maxResults", "250"), ("fields", "items(summary,status,start,end),nextPageToken"),
        ], pageToken));

    /// <summary>The ids of the calendars switched on in Google Calendar (selected, not deleted), and the next page.</summary>
    public static (IReadOnlyList<string> Ids, string? Next) ParseCalendarList(string json)
    {
        var ids = new List<string>();
        var next = Items(json, item =>
        {
            if (GoogleOAuth.Str(item, "id") is { Length: > 0 } id && Bool(item, "selected") && !Bool(item, "deleted")) ids.Add(id);
        });
        return (ids, next);
    }

    /// <summary>The events (cancelled ones and ones without a readable start / end skipped), and the next page.</summary>
    public static (IReadOnlyList<CalEvent> Events, string? Next) ParseEvents(string json, TimeZoneInfo zone)
    {
        var events = new List<CalEvent>();
        var next = Items(json, item =>
        {
            if (GoogleOAuth.Str(item, "status") == "cancelled") return;
            if (!item.TryGetProperty("start", out var s) || !item.TryGetProperty("end", out var e)) return;
            if (Time(s, zone) is not { } start || Time(e, zone) is not { } end) return;
            var title = GoogleOAuth.Str(item, "summary");
            events.Add(new CalEvent(string.IsNullOrWhiteSpace(title) ? "(No title)" : title.Trim(), start.At, end.At, start.AllDay));
        });
        return (events, next);
    }

    /// <summary>What to fetch: from a week before the 1st of <paramref name="now"/>'s month to 60 days ahead.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) Window(DateTimeOffset now) =>
        (new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddDays(-7), now.AddDays(60));

    /// <summary>Every local day an event touches (an all-day end is exclusive, a timed event ending at 00:00 too).</summary>
    public static IReadOnlySet<DateOnly> DaysWithEvents(IEnumerable<CalEvent> events, TimeZoneInfo zone)
    {
        var days = new HashSet<DateOnly>();
        foreach (var e in events)
        {
            var first = Day(e.Start, zone);
            var last = e.End > e.Start ? Day(e.End.AddTicks(-1), zone) : first;
            for (var d = first; d <= last && days.Count < 4000; d = d.AddDays(1)) days.Add(d);
        }
        return days;
    }

    /// <summary>The event that starts first among those not ended yet (one in progress counts); null when none.</summary>
    public static CalEvent? NextEvent(IEnumerable<CalEvent> events, DateTimeOffset now) =>
        events.Where(e => e.End > now || (e.End <= e.Start && e.Start >= now))
            .OrderBy(e => e.Start).ThenBy(e => e.AllDay ? 0 : 1).FirstOrDefault();

    /// <summary>"NOW", "TODAY 14:30", "TODAY", "TOMORROW 09:00", "TOMORROW", or "TUE 24".</summary>
    public static string WhenLabel(CalEvent e, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!e.AllDay && e.Start <= now && e.End > now) return "NOW";
        var today = Day(now, zone);
        var day = e.AllDay && e.Start <= now ? today : Day(e.Start, zone);
        var time = e.AllDay ? "" : " " + TimeZoneInfo.ConvertTime(e.Start, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
        if (day == today) return "TODAY" + time;
        if (day == today.AddDays(1)) return "TOMORROW" + time;
        return day.ToString("ddd d", CultureInfo.InvariantCulture).ToUpperInvariant();
    }

    private static DateOnly Day(DateTimeOffset t, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, zone).DateTime);

    private static (DateTimeOffset At, bool AllDay)? Time(JsonElement o, TimeZoneInfo zone)
    {
        if (o.ValueKind != JsonValueKind.Object) return null;
        if (GoogleOAuth.Str(o, "dateTime") is { } dt)
            return DateTimeOffset.TryParse(dt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? (t, false) : null;
        if (GoogleOAuth.Str(o, "date") is { } d &&
            DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            var local = day.ToDateTime(TimeOnly.MinValue);
            return (new DateTimeOffset(local, zone.GetUtcOffset(local)), true);
        }
        return null;
    }

    // Walks "items" (an array of objects) and returns "nextPageToken"; anything malformed is skipped.
    private static string? Items(string json, Action<JsonElement> each)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object) each(item);
            return GoogleOAuth.Str(root, "nextPageToken");
        }
        catch (JsonException) { return null; }
    }

    private static bool Bool(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Rfc3339(DateTimeOffset t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static (string, string)[] Pairs((string, string)[] fixedPairs, string? pageToken) =>
        pageToken is null ? fixedPairs : [.. fixedPairs, ("pageToken", pageToken)];
}
