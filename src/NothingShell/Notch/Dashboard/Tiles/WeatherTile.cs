using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NothingShell.Core.Forecast;
using NothingShell.Core.Notch;
using NothingShell.Forecast;

namespace NothingShell.Notch.Dashboard.Tiles;

/// <summary>
/// Weather tile (mockup <c>NR.weather</c>, spec §6.5): 1×1 temperature in dots + city; 2×1 icon, dots, city and
/// ↑high ↓low; 2×2 the same plus a 5-day row. No city yet: "SET A CITY" (click opens Settings). A report older than an
/// hour (offline) is dimmed and says "UPDATED HH:MM".
/// </summary>
public sealed class WeatherTile : ITileView
{
    private readonly WeatherService _weather;
    private readonly Action _openSettings;
    private TileSize _size;
    private Border? _tile;

    public WeatherTile(WeatherService weather, Action openSettings)
    {
        _weather = weather;
        _openSettings = openSettings;
        _weather.Changed += Fill;
    }

    public TileKind Kind => TileKind.Weather;

    public static string IconFor(WeatherKind k) => k switch
    {
        WeatherKind.Clear => "sun",
        WeatherKind.PartlyCloudy => "cloudsun",
        WeatherKind.Fog => "fog",
        WeatherKind.Drizzle or WeatherKind.Rain => "rain",
        WeatherKind.Snow => "snow",
        WeatherKind.Storm => "storm",
        _ => "cloud",
    };

    public FrameworkElement Build(TileSize size)
    {
        _size = size;
        _tile = TileChrome.Tile(new Grid(), center: size == TileSize.One);
        Fill();
        return _tile;
    }

    private void Fill()
    {
        if (_tile is null) return;
        var place = _weather.Place;
        var r = _weather.Current;
        if (place is null || r is null)
        {
            var hint = TileChrome.Label(place is null ? "Set a city" : "No data yet");
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.VerticalAlignment = VerticalAlignment.Center;
            var box = new Grid { Background = System.Windows.Media.Brushes.Transparent, Cursor = place is null ? Cursors.Hand : null, Children = { hint } };
            if (place is null) box.MouseLeftButtonUp += (_, e) => { e.Handled = true; _openSettings(); };
            _tile.Child = box;
            return;
        }
        var stale = WeatherRules.IsStale(r, DateTimeOffset.Now);
        FrameworkElement content = _size switch
        {
            TileSize.One => One(place, r, stale),
            TileSize.Wide => Wide(place, r, stale),
            _ => Big(place, r, stale),
        };
        content.Opacity = stale ? 0.5 : 1;
        _tile.Child = content;
    }

    private static string Deg(int t) => t.ToString(CultureInfo.InvariantCulture) + "°";

    private static string Updated(WeatherReport r) => "Updated " + r.FetchedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    private static FrameworkElement One(WeatherPlace p, WeatherReport r, bool stale)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new DotMatrix { Text = Deg(r.Temp), Dot = 3, Gap = 1, Fill = TileChrome.Text, HorizontalAlignment = HorizontalAlignment.Center });
        var label = TileChrome.Label(stale ? Updated(r) : p.Name);
        label.Margin = new Thickness(0, 6, 0, 0);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(label);
        return stack;
    }

    private static FrameworkElement Wide(WeatherPlace p, WeatherReport r, bool stale)
    {
        var row = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        var icon = TileChrome.Icon(IconFor(r.Kind), 20);
        icon.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(icon, System.Windows.Controls.Dock.Left);
        row.Children.Add(icon);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var city = TileChrome.Label(p.Name);
        city.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(city);
        var range = TileChrome.Body($"↑{Deg(r.High)} ↓{Deg(r.Low)}", 11, FontWeights.Normal, TileChrome.Frozen(0xCC, 0xCC, 0xCC));
        range.Margin = new Thickness(0, 3, 0, 0);
        range.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(range);
        if (stale)
        {
            var upd = TileChrome.Label(Updated(r));
            upd.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(upd);
        }
        DockPanel.SetDock(right, System.Windows.Controls.Dock.Right);
        row.Children.Add(right);
        // Dot 3 / gap 1.1 (not the mockup's 4 / 1.4): "-12°" must fit beside the city column in a 178 DIP tile.
        row.Children.Add(new DotMatrix { Text = Deg(r.Temp), Dot = 3, Gap = 1.1, Fill = TileChrome.Text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static FrameworkElement Big(WeatherPlace p, WeatherReport r, bool stale)
    {
        var panel = new DockPanel();
        var top = Wide(p, r, stale);
        top.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(top, System.Windows.Controls.Dock.Top);
        panel.Children.Add(top);
        var days = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, VerticalAlignment = VerticalAlignment.Bottom };
        foreach (var d in r.Days.Take(5))
        {
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var name = TileChrome.Label(d.Date.ToString("ddd", CultureInfo.InvariantCulture)[..1]);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(name);
            var ic = TileChrome.Icon(IconFor(d.Kind), 16);
            ic.Margin = new Thickness(0, 4, 0, 4);
            col.Children.Add(ic);
            var hi = TileChrome.Body(Deg(d.High), 11, FontWeights.Normal);
            hi.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(hi);
            days.Children.Add(col);
        }
        panel.Children.Add(days);
        return panel;
    }

    public void Opened() => Fill();
    public void Closed() { }
    public void Dispose() => _weather.Changed -= Fill;
}
