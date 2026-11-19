using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using NothingShell.Core.Forecast;

namespace NothingShell.Forecast;

/// <summary>
/// Weather for the notch tile (spec §6.5): Open-Meteo, no key; refreshes now and every 30 minutes; the last report is
/// cached in weather.json so it shows at once after a restart and stays (dimmed) while offline. Requests carry a
/// generic user agent and the coordinates only. The city is never logged.
/// </summary>
public sealed class WeatherService : IDisposable
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _cachePath;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    public WeatherService(string dataDir)
    {
        _cachePath = Path.Combine(dataDir, "weather.json");
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public WeatherPlace? Place { get; private set; }
    public WeatherReport? Current { get; private set; }

    /// <summary>Raised on the UI thread when the place or the report changed.</summary>
    public event Action? Changed;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("NothingShell/1.0");
        return c;
    }

    /// <summary>Looks up <paramref name="city"/>: the place, or null with Offline telling "no network" from "not found".</summary>
    public async Task<(WeatherPlace? Place, bool Offline)> FindAsync(string city)
    {
        if (string.IsNullOrWhiteSpace(city)) return (null, false);
        try
        {
            var json = await Http.GetStringAsync(WeatherRules.GeocodeUrl(city));
            return (WeatherRules.ParseGeocode(json), false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Log.Info("weather: geocoding failed (offline?)");
            return (null, true);
        }
    }

    /// <summary>Follows <paramref name="place"/> (null: nothing to show). Loads the cache for it, then refreshes.</summary>
    public void Start(WeatherPlace? place)
    {
        Place = place;
        _timer.Stop();
        Current = place is null ? null : LoadCache(place);
        Changed?.Invoke();
        if (place is null) return;
        _timer.Start();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var place = Place;
        if (place is null || _disposed) return;
        try
        {
            var json = await Http.GetStringAsync(WeatherRules.ForecastUrl(place));
            var report = WeatherRules.ParseForecast(json, DateTimeOffset.Now);
            if (report is null) { Log.Info("weather: unexpected response"); return; }
            if (!Equals(place, Place) || _disposed) return; // the place changed meanwhile
            Current = report;
            SaveCache(place, report);
            Changed?.Invoke();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Log.Info("weather: refresh failed (offline?), showing the last report");
        }
    }

    private sealed record Cache(WeatherPlace Place, WeatherReport Report);

    private WeatherReport? LoadCache(WeatherPlace place)
    {
        try
        {
            if (!File.Exists(_cachePath)) return null;
            var c = JsonSerializer.Deserialize<Cache>(File.ReadAllText(_cachePath), Json);
            return c is not null && Equals(c.Place, place) ? c.Report : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private void SaveCache(WeatherPlace place, WeatherReport report)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var temp = _cachePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Cache(place, report), Json));
            File.Move(temp, _cachePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("weather cache write failed", e); }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }
}
