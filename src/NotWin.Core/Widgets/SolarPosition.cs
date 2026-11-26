namespace NotWin.Core.Widgets;

/// <summary>
/// Where the sun is (Plan 13, sun compass), from NOAA's general solar position equations (fractional year, equation of
/// time, declination, hour angle). Accurate to well under a degree / a couple of minutes, which is plenty for a tile.
/// </summary>
public static class SolarPosition
{
    private const double Deg = Math.PI / 180;

    /// <summary>Azimuth (degrees clockwise from north) and elevation (degrees above the horizon) at <paramref name="utc"/>.</summary>
    public static (double Azimuth, double Elevation) Position(DateTime utc, double lat, double lon)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var (eqTime, decl) = Sun(utc.DayOfYear, utc.TimeOfDay.TotalHours, DaysInYear(utc.Year));
        var trueSolarMinutes = utc.TimeOfDay.TotalMinutes + eqTime + 4 * lon;
        var hourAngle = (trueSolarMinutes / 4 - 180) * Deg;
        var phi = lat * Deg;
        var cosZenith = Math.Sin(phi) * Math.Sin(decl) + Math.Cos(phi) * Math.Cos(decl) * Math.Cos(hourAngle);
        var elevation = 90 - Math.Acos(Math.Clamp(cosZenith, -1, 1)) / Deg;
        var azimuth = Math.Atan2(Math.Sin(hourAngle), Math.Cos(hourAngle) * Math.Sin(phi) - Math.Tan(decl) * Math.Cos(phi)) / Deg + 180;
        return (Normalize(azimuth), elevation);
    }

    /// <summary>Sunrise and sunset on <paramref name="date"/> in <paramref name="zone"/>; null on polar day or night.</summary>
    public static (TimeOnly Rise, TimeOnly Set)? SunTimes(DateOnly date, double lat, double lon, TimeZoneInfo zone)
    {
        var (eqTime, decl) = Sun(date.DayOfYear, 12, DaysInYear(date.Year));
        var phi = lat * Deg;
        var cosHa = Math.Cos(90.833 * Deg) / (Math.Cos(phi) * Math.Cos(decl)) - Math.Tan(phi) * Math.Tan(decl);
        if (cosHa is < -1 or > 1) return null;
        var ha = Math.Acos(cosHa) / Deg;
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        TimeOnly Local(double utcMinutes) =>
            TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(midnight.AddMinutes(utcMinutes), zone));
        // Seen from the zone, a place near the date line can have its sunrise on the previous UTC day: DateTime arithmetic
        // takes care of minutes below 0 or above 1440.
        return (Local(720 - 4 * (lon + ha) - eqTime), Local(720 - 4 * (lon - ha) - eqTime));
    }

    // Equation of time (minutes) and declination (radians) for a day of the year and an hour (UTC).
    private static (double EqTime, double Decl) Sun(int dayOfYear, double hour, int daysInYear)
    {
        var g = 2 * Math.PI / daysInYear * (dayOfYear - 1 + (hour - 12) / 24);
        var eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g) - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
        var decl = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g) + 0.000907 * Math.Sin(2 * g)
                   - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);
        return (eqTime, decl);
    }

    private static int DaysInYear(int year) => DateTime.IsLeapYear(year) ? 366 : 365;

    private static double Normalize(double deg) => (deg % 360 + 360) % 360;
}
