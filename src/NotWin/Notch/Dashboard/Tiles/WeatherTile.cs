using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotWin.Core.Forecast;
using NotWin.Core.Notch;
using NotWin.Forecast;

namespace NotWin.Notch.Dashboard.Tiles;

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
        _tile = DashChrome.Tile(new Grid(), center: size == TileSize.One);
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
            var hint = DashChrome.Label(place is null ? "Set a city" : "No data yet");
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
        stack.Children.Add(new DotMatrix { Text = Deg(r.Temp), Dot = 3, Gap = 1, Fill = DashChrome.Text, HorizontalAlignment = HorizontalAlignment.Center });
        var label = DashChrome.Label(stale ? Updated(r) : p.Name);
        label.Margin = new Thickness(0, 6, 0, 0);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(label);
        return stack;
    }

    // Two rows (user 2026-11-27: the temperature overlapped the city, then the city was cut to "GHA…"):
    // icon and city on top (the city shortened with "…" only when it is really long, an "Updated" time on the right when
    // stale), the dot temperature below with the high and low stacked beside it.
    internal static FrameworkElement Wide(WeatherPlace p, WeatherReport r, bool stale)
    {
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = DashChrome.Icon(IconFor(r.Kind), 16);
        icon.Margin = new Thickness(0, 0, 6, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(icon);
        var city = DashChrome.Label(p.Name);
        city.TextTrimming = TextTrimming.CharacterEllipsis;
        city.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(city, 1);
        top.Children.Add(city);
        if (stale)
        {
            var upd = DashChrome.Label(Updated(r));
            upd.Margin = new Thickness(6, 0, 0, 0);
            upd.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(upd, 2);
            top.Children.Add(upd);
        }
        grid.Children.Add(top);

        var bottom = new Grid();
        Grid.SetRow(bottom, 1);
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
        var temp = new DotMatrix { Text = Deg(r.Temp), Dot = 3, Gap = 1.1, Fill = DashChrome.Text, VerticalAlignment = VerticalAlignment.Bottom };
        bottom.Children.Add(temp);
        var range = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 0) };
        foreach (var line in new[] { $"↑{Deg(r.High)}", $"↓{Deg(r.Low)}" })
        {
            var t = DashChrome.Body(line, 11, FontWeights.Normal, DashChrome.Muted);
            t.HorizontalAlignment = HorizontalAlignment.Right;
            range.Children.Add(t);
        }
        Grid.SetColumn(range, 1);
        bottom.Children.Add(range);
        grid.Children.Add(bottom);
        return grid;
    }

    internal static FrameworkElement Big(WeatherPlace p, WeatherReport r, bool stale)
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
            var name = DashChrome.Label(d.Date.ToString("ddd", CultureInfo.InvariantCulture)[..1]);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(name);
            var ic = DashChrome.Icon(IconFor(d.Kind), 16);
            ic.Margin = new Thickness(0, 4, 0, 4);
            col.Children.Add(ic);
            var hi = DashChrome.Body(Deg(d.High), 11, FontWeights.Normal);
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
