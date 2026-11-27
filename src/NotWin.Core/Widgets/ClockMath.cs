using System.Globalization;

namespace NotWin.Core.Widgets;

/// <summary>Analogue clock hand angles (Plan 13): degrees clockwise from 12.</summary>
public static class ClockHands
{
    /// <summary>The hour hand moves with the minutes, the minute hand with the seconds; the seconds hand jumps.</summary>
    public static (double Hour, double Minute, double Second) Angles(TimeOnly t)
    {
        var seconds = t.Second;
        var minutes = t.Minute + seconds / 60.0;
        var hours = t.Hour % 12 + minutes / 60.0;
        return (hours * 30, minutes * 6, seconds * 6.0);
    }
}

/// <summary>World-clock labels (Plan 13): "GMT+5:30" and the difference from local time ("+4H 30M").</summary>
public static class ZoneLabels
{
    private const char Minus = '−';

    /// <summary>The zone's offset from UTC at <paramref name="utcNow"/> (daylight saving applied).</summary>
    public static TimeSpan Offset(TimeZoneInfo zone, DateTime utcNow) => zone.GetUtcOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));

    /// <summary>"GMT", "GMT+1", "GMT−3", "GMT+5:30".</summary>
    public static string GmtLabel(TimeSpan offset) => offset == TimeSpan.Zero ? "GMT" : "GMT" + Signed(offset);

    /// <summary>The zone relative to local time: "+4H 30M", "−1H", or "SAME TIME".</summary>
    public static string Difference(TimeSpan local, TimeSpan zone)
    {
        var d = zone - local;
        if (d == TimeSpan.Zero) return "SAME TIME";
        var abs = d.Duration();
        var sign = d < TimeSpan.Zero ? Minus : '+';
        var h = (int)abs.TotalHours;
        var m = abs.Minutes;
        var text = h > 0 && m > 0 ? $"{h}H {m}M" : h > 0 ? $"{h}H" : $"{m}M";
        return sign + text;
    }

    private static string Signed(TimeSpan offset)
    {
        var abs = offset.Duration();
        var sign = offset < TimeSpan.Zero ? Minus : '+';
        var h = ((int)abs.TotalHours).ToString(CultureInfo.InvariantCulture);
        return abs.Minutes == 0 ? $"{sign}{h}" : $"{sign}{h}:{abs.Minutes:00}";
    }
}
