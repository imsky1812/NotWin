using System.Globalization;
using System.Text.Json;

namespace NothingShell.Core.Alarms;

/// <summary>
/// One of Nothing Shell's own alarms (Plan 9; Windows' Clock app does not share its alarms). <paramref name="Days"/>:
/// bit 0 = Sunday … bit 6 = Saturday; 0 = once (turned off after it rings). Times are local wall-clock time.
/// </summary>
public sealed record Alarm(string Id, int Hour, int Minute, int Days, bool On);

public static class AlarmRules
{
    private const int EveryDay = 0b1111111, Weekdays = 0b0111110, Weekends = 0b1000001;

    /// <summary>The first ring strictly after <paramref name="after"/>; null when the alarm is off.</summary>
    public static DateTime? NextRing(Alarm a, DateTime after)
    {
        if (!a.On) return null;
        for (var d = 0; d <= 7; d++)
        {
            var day = after.Date.AddDays(d);
            var at = day.AddHours(a.Hour).AddMinutes(a.Minute);
            if (at <= after) continue;
            if (a.Days == 0 || (a.Days & (1 << (int)day.DayOfWeek)) != 0) return at;
        }
        return null;
    }

    /// <summary>The alarm that rings next and when; null when none is on.</summary>
    public static (Alarm Alarm, DateTime At)? Next(IEnumerable<Alarm> all, DateTime now)
    {
        (Alarm, DateTime)? best = null;
        foreach (var a in all)
            if (NextRing(a, now) is { } at && (best is null || at < best.Value.Item2)) best = (a, at);
        return best;
    }

    /// <summary>The alarms with a ring in (<paramref name="lastCheck"/>, <paramref name="now"/>].</summary>
    public static IReadOnlyList<Alarm> Due(IEnumerable<Alarm> all, DateTime lastCheck, DateTime now) =>
        [.. all.Where(a => NextRing(a, lastCheck) is { } at && at <= now)];

    public static string DaysLabel(int days) => days switch
    {
        0 => "ONCE",
        EveryDay => "EVERY DAY",
        Weekdays => "WEEKDAYS",
        Weekends => "WEEKENDS",
        _ => string.Join(" ", Enumerable.Range(0, 7).Where(i => (days & (1 << i)) != 0)
            .Select(i => CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames[i].ToUpperInvariant())),
    };

    /// <summary>"IN 25 MIN", "IN 8 H", "IN 2 D".</summary>
    public static string Until(DateTime at, DateTime now)
    {
        var t = at - now;
        if (t < TimeSpan.FromHours(1)) return $"IN {Math.Max(1, (int)Math.Ceiling(t.TotalMinutes))} MIN";
        if (t < TimeSpan.FromHours(48)) return $"IN {(int)t.TotalHours} H";
        return $"IN {(int)t.TotalDays} D";
    }

    /// <summary>"7:30", "07:30" or "730" → (7, 30); anything else (25:00, 7, abc) → null.</summary>
    public static (int H, int M)? ParseTime(string text)
    {
        var t = text.Trim();
        string hs, ms;
        var colon = t.IndexOf(':');
        if (colon >= 0) { hs = t[..colon]; ms = t[(colon + 1)..]; }
        else if (t.Length is 3 or 4 && t.All(char.IsAsciiDigit)) { hs = t[..^2]; ms = t[^2..]; }
        else return null;
        if (hs.Length is < 1 or > 2 || ms.Length != 2 || !hs.All(char.IsAsciiDigit) || !ms.All(char.IsAsciiDigit)) return null;
        int h = int.Parse(hs, CultureInfo.InvariantCulture), m = int.Parse(ms, CultureInfo.InvariantCulture);
        return h is >= 0 and <= 23 && m is >= 0 and <= 59 ? (h, m) : null;
    }
}

/// <summary>The alarms in <c>alarms.json</c> (written atomically); a missing or bad file is an empty list.</summary>
public sealed class AlarmStore(string dir)
{
    public const string FileName = "alarms.json";

    public IReadOnlyList<Alarm> Load()
    {
        try
        {
            var path = Path.Combine(dir, FileName);
            if (!File.Exists(path)) return [];
            var list = JsonSerializer.Deserialize<List<Alarm>>(File.ReadAllText(path), JsonFile.Options);
            return list?.Where(a => a is { Id: not null } && a.Hour is >= 0 and <= 23 && a.Minute is >= 0 and <= 59).ToList() ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<Alarm> alarms) => JsonFile.WriteAtomic(dir, FileName, alarms);
}
