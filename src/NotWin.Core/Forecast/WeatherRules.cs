using System.Globalization;
using System.Text.Json;

namespace NotWin.Core.Forecast;

/// <summary>A place the user typed in Settings, resolved by Open-Meteo's geocoder.</summary>
public sealed record WeatherPlace(string Name, string Country, double Lat, double Lon);

public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Storm }

public sealed record WeatherDay(DateOnly Date, int High, int Low, WeatherKind Kind);

public sealed record WeatherReport(int Temp, int High, int Low, WeatherKind Kind, bool IsDay, IReadOnlyList<WeatherDay> Days,
    DateTimeOffset FetchedAt);

/// <summary>
/// Open-Meteo (spec §6.5: free, no API key): request URLs, response parsing and the WMO weather-code mapping.
/// Temperatures in °C, rounded half away from zero.
/// </summary>
public static class WeatherRules
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    public static WeatherKind Kind(int wmoCode) => wmoCode switch
    {
        0 => WeatherKind.Clear,
        1 or 2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        >= 51 and <= 57 => WeatherKind.Drizzle,
        (>= 61 and <= 67) or (>= 80 and <= 82) => WeatherKind.Rain,
        (>= 71 and <= 77) or 85 or 86 => WeatherKind.Snow,
        >= 95 and <= 99 => WeatherKind.Storm,
        _ => WeatherKind.Cloudy,
    };

    public static string GeocodeUrl(string city) =>
        $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city.Trim())}&count=1&language=en&format=json";

    public static string ForecastUrl(WeatherPlace p) => string.Create(CultureInfo.InvariantCulture,
        $"https://api.open-meteo.com/v1/forecast?latitude={p.Lat}&longitude={p.Lon}&current=temperature_2m,weather_code,is_day&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=5");

    public static WeatherPlace? ParseGeocode(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
            var r = results[0];
            return new WeatherPlace(
                r.GetProperty("name").GetString() ?? "",
                r.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "",
                r.GetProperty("latitude").GetDouble(),
                r.GetProperty("longitude").GetDouble());
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    public static WeatherReport? ParseForecast(string json, DateTimeOffset fetchedAt)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("current", out var cur) || !root.TryGetProperty("daily", out var daily)) return null;
            var dates = daily.GetProperty("time");
            var codes = daily.GetProperty("weather_code");
            var highs = daily.GetProperty("temperature_2m_max");
            var lows = daily.GetProperty("temperature_2m_min");
            var days = new List<WeatherDay>();
            for (var i = 0; i < dates.GetArrayLength(); i++)
                days.Add(new WeatherDay(
                    DateOnly.ParseExact(dates[i].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Round(highs[i].GetDouble()), Round(lows[i].GetDouble()), Kind(codes[i].GetInt32())));
            if (days.Count == 0) return null;
            return new WeatherReport(
                Round(cur.GetProperty("temperature_2m").GetDouble()),
                days[0].High, days[0].Low,
                Kind(cur.GetProperty("weather_code").GetInt32()),
                cur.TryGetProperty("is_day", out var d) && d.GetInt32() == 1,
                days, fetchedAt);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException
                                   or IndexOutOfRangeException) { return null; }
    }

    public static bool IsStale(WeatherReport r, DateTimeOffset now) => now - r.FetchedAt > StaleAfter;

    /// <summary>The dot-font glyph for the widget's condition icon (Notch.DotFont letters c m r s w t f).</summary>
    public static char Glyph(WeatherKind kind, bool isDay) => kind switch
    {
        WeatherKind.Clear => isDay ? 's' : 'm',
        WeatherKind.Drizzle or WeatherKind.Rain => 'r',
        WeatherKind.Snow => 'w',
        WeatherKind.Storm => 't',
        WeatherKind.Fog => 'f',
        _ => 'c',
    };

    private static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
}
