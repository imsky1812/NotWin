using System.Globalization;
using NothingShell.Core.Forecast;

namespace NothingShell.Core.Tests.Forecast;

public sealed class WeatherRulesTests
{
    private static readonly DateTimeOffset Fetched = new(2026, 11, 19, 10, 0, 0, TimeSpan.FromHours(5.5));

    // Trimmed from a real Open-Meteo forecast response (Delhi).
    private const string Forecast = """
        {"latitude":28.625,"longitude":77.25,"timezone":"Asia/Kolkata","utc_offset_seconds":19800,
         "current_units":{"time":"iso8601","temperature_2m":"°C","weather_code":"wmo code","is_day":""},
         "current":{"time":"2026-11-19T10:00","interval":900,"temperature_2m":24.5,"weather_code":2,"is_day":1},
         "daily":{"time":["2026-11-19","2026-11-20","2026-11-21","2026-11-22","2026-11-23"],
                  "weather_code":[2,0,3,61,95],
                  "temperature_2m_max":[28.4,27.9,26.1,22.0,21.5],
                  "temperature_2m_min":[23.1,22.6,21.0,18.4,-0.6]}}
        """;

    private const string Geocode = """
        {"results":[{"id":1273294,"name":"Delhi","latitude":28.65195,"longitude":77.23149,"elevation":227.0,
                     "feature_code":"PPLC","country_code":"IN","timezone":"Asia/Kolkata","country":"India","admin1":"Delhi"}],
         "generationtime_ms":0.6}
        """;

    [Theory]
    [InlineData(0, WeatherKind.Clear)]
    [InlineData(1, WeatherKind.PartlyCloudy)]
    [InlineData(2, WeatherKind.PartlyCloudy)]
    [InlineData(3, WeatherKind.Cloudy)]
    [InlineData(45, WeatherKind.Fog)]
    [InlineData(48, WeatherKind.Fog)]
    [InlineData(51, WeatherKind.Drizzle)]
    [InlineData(57, WeatherKind.Drizzle)]
    [InlineData(61, WeatherKind.Rain)]
    [InlineData(67, WeatherKind.Rain)]
    [InlineData(80, WeatherKind.Rain)]
    [InlineData(82, WeatherKind.Rain)]
    [InlineData(71, WeatherKind.Snow)]
    [InlineData(77, WeatherKind.Snow)]
    [InlineData(85, WeatherKind.Snow)]
    [InlineData(86, WeatherKind.Snow)]
    [InlineData(95, WeatherKind.Storm)]
    [InlineData(99, WeatherKind.Storm)]
    public void Kind_MapsWmoCodes(int code, WeatherKind kind) => Assert.Equal(kind, WeatherRules.Kind(code));

    [Theory]
    [InlineData(WeatherKind.Clear, true, 's')]
    [InlineData(WeatherKind.Clear, false, 'm')]
    [InlineData(WeatherKind.PartlyCloudy, true, 'c')]
    [InlineData(WeatherKind.Cloudy, false, 'c')]
    [InlineData(WeatherKind.Drizzle, true, 'r')]
    [InlineData(WeatherKind.Rain, true, 'r')]
    [InlineData(WeatherKind.Snow, true, 'w')]
    [InlineData(WeatherKind.Storm, true, 't')]
    [InlineData(WeatherKind.Fog, true, 'f')]
    public void Glyph_MapsKinds(WeatherKind kind, bool isDay, char glyph) => Assert.Equal(glyph, WeatherRules.Glyph(kind, isDay));

    [Fact]
    public void ParseForecast_ReadsCurrentAndFiveDays()
    {
        var r = WeatherRules.ParseForecast(Forecast, Fetched)!;
        Assert.Equal(25, r.Temp);                 // 24.5 rounds away from zero
        Assert.Equal(WeatherKind.PartlyCloudy, r.Kind);
        Assert.True(r.IsDay);
        Assert.Equal(28, r.High);
        Assert.Equal(23, r.Low);
        Assert.Equal(5, r.Days.Count);
        Assert.Equal(new DateOnly(2026, 11, 23), r.Days[4].Date);
        Assert.Equal(WeatherKind.Storm, r.Days[4].Kind);
        Assert.Equal(-1, r.Days[4].Low);
        Assert.Equal(Fetched, r.FetchedAt);
    }

    [Fact]
    public void ParseForecast_Garbage_Null()
    {
        Assert.Null(WeatherRules.ParseForecast("{not json", Fetched));
        Assert.Null(WeatherRules.ParseForecast("{\"error\":true,\"reason\":\"bad\"}", Fetched));
    }

    [Fact]
    public void ParseGeocode_FirstResult()
    {
        var p = WeatherRules.ParseGeocode(Geocode)!;
        Assert.Equal("Delhi", p.Name);
        Assert.Equal("India", p.Country);
        Assert.Equal(28.65195, p.Lat, 5);
        Assert.Equal(77.23149, p.Lon, 5);
    }

    [Fact]
    public void ParseGeocode_NoResults_Null()
    {
        Assert.Null(WeatherRules.ParseGeocode("{\"generationtime_ms\":0.3}"));
        Assert.Null(WeatherRules.ParseGeocode("garbage"));
    }

    [Fact]
    public void ForecastUrl_UsesInvariantDecimals()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var url = WeatherRules.ForecastUrl(new WeatherPlace("Delhi", "India", 28.65195, 77.23149));
            Assert.Contains("latitude=28.65195&longitude=77.23149", url);
            Assert.StartsWith("https://api.open-meteo.com/v1/forecast?", url);
            Assert.Contains("forecast_days=5", url);
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void GeocodeUrl_EscapesCity() =>
        Assert.Equal("https://geocoding-api.open-meteo.com/v1/search?name=New%20Delhi&count=1&language=en&format=json",
            WeatherRules.GeocodeUrl("New Delhi"));

    [Fact]
    public void IsStale_After1Hour()
    {
        var r = WeatherRules.ParseForecast(Forecast, Fetched)!;
        Assert.False(WeatherRules.IsStale(r, Fetched.AddMinutes(59)));
        Assert.True(WeatherRules.IsStale(r, Fetched.AddMinutes(61)));
    }
}
