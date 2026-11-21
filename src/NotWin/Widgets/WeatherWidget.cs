using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NotWin.Core.Forecast;
using NotWin.Core.Widgets;
using NotWin.Forecast;
using NotWin.Notch;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// Weather widget (spec §6.5, mockup <c>renderWeather</c>): a dot-drawn condition icon, the temperature, high/low and a
/// 5-day row, from the same <see cref="WeatherService"/> as the notch tile. 1×1 icon + temperature in dots + city;
/// 2×1 icon, temperature, hi/lo, city and the 5-day row; 2×2 city + condition, a big icon and temperature, hi/lo and the
/// 5-day row. No city: a hint to set one in Settings. Offline (over an hour old): dimmed with "UPDATED HH:MM".
/// </summary>
public sealed class WeatherWidget : IDesktopWidget
{
    private readonly WeatherService _weather;
    private Border? _host;
    private WidgetSize _size;

    public WeatherWidget(WeatherService weather)
    {
        _weather = weather;
        _weather.Changed += Fill;
    }

    public WidgetKind Kind => WidgetKind.Weather;

    public FrameworkElement Build(WidgetSize size)
    {
        _size = size;
        _host = new Border();
        Fill();
        return _host;
    }

    private static string Deg(int t) => t.ToString(CultureInfo.InvariantCulture) + "°";

    private static string Condition(WeatherKind k) => k switch
    {
        WeatherKind.Clear => "Clear",
        WeatherKind.PartlyCloudy => "Partly cloudy",
        WeatherKind.Cloudy => "Overcast",
        WeatherKind.Fog => "Fog",
        WeatherKind.Drizzle => "Drizzle",
        WeatherKind.Rain => "Rain",
        WeatherKind.Snow => "Snow",
        _ => "Storm",
    };

    private static DotMatrix Dots(string text, double dot, double gap) =>
        new() { Text = text, Dot = dot, Gap = gap, Fill = TileChrome.Text, VerticalAlignment = VerticalAlignment.Center };

    private void Fill()
    {
        if (_host is null) return;
        var place = _weather.Place;
        var r = _weather.Current;
        if (place is null || r is null)
        {
            var hint = TileChrome.Label(place is null ? "Set a city in Settings" : "No data yet");
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.TextWrapping = TextWrapping.Wrap;
            hint.TextAlignment = TextAlignment.Center;
            _host.Child = hint;
            return;
        }
        var stale = WeatherRules.IsStale(r, DateTimeOffset.Now);
        var glyph = WeatherRules.Glyph(r.Kind, r.IsDay).ToString();
        FrameworkElement content = _size switch
        {
            { W: 1 } => One(place, r, glyph, stale),
            { H: 2 } => Big(place, r, glyph, stale),
            _ => Wide(place, r, glyph, stale),
        };
        content.Opacity = stale ? 0.5 : 1;
        _host.Child = content;
    }

    private static string Updated(WeatherReport r) => "Updated " + r.FetchedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    private static FrameworkElement One(WeatherPlace p, WeatherReport r, string glyph, bool stale)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var g = Dots(glyph, 4, 1.4);
        g.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(g);
        var t = Dots(Deg(r.Temp), 3.4, 1.2);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 8, 0, 8);
        stack.Children.Add(t);
        var city = TileChrome.Label(stale ? Updated(r) : p.Name);
        city.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(city);
        return stack;
    }

    private static FrameworkElement Wide(WeatherPlace p, WeatherReport r, string glyph, bool stale)
    {
        var panel = new DockPanel();
        var days = Days(r);
        DockPanel.SetDock(days, System.Windows.Controls.Dock.Bottom);
        panel.Children.Add(days);
        var top = new DockPanel { VerticalAlignment = VerticalAlignment.Top };
        var city = TileChrome.Label(stale ? Updated(r) : p.Name);
        city.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(city, System.Windows.Controls.Dock.Right);
        top.Children.Add(city);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Dots(glyph, 4, 1.5);
        g.Margin = new Thickness(0, 0, 14, 0);
        row.Children.Add(g);
        var temp = TileChrome.Body(Deg(r.Temp), 34, FontWeights.Medium);
        temp.VerticalAlignment = VerticalAlignment.Center;
        temp.Margin = new Thickness(0, 0, 14, 0);
        row.Children.Add(temp);
        var hl = TileChrome.Body($"↑ {Deg(r.High)}\n↓ {Deg(r.Low)}", 12, FontWeights.Normal, TileChrome.Frozen(0xCF, 0xCF, 0xCF));
        hl.VerticalAlignment = VerticalAlignment.Center;
        hl.LineHeight = 18;
        row.Children.Add(hl);
        top.Children.Add(row);
        panel.Children.Add(top);
        return panel;
    }

    private static FrameworkElement Big(WeatherPlace p, WeatherReport r, string glyph, bool stale)
    {
        var panel = new DockPanel();
        var head = new DockPanel();
        var cond = TileChrome.Label(stale ? Updated(r) : Condition(r.Kind));
        DockPanel.SetDock(cond, System.Windows.Controls.Dock.Right);
        head.Children.Add(cond);
        head.Children.Add(TileChrome.Label(p.Name));
        DockPanel.SetDock(head, System.Windows.Controls.Dock.Top);
        panel.Children.Add(head);
        var days = Days(r);
        DockPanel.SetDock(days, System.Windows.Controls.Dock.Bottom);
        panel.Children.Add(days);
        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var g = Dots(glyph, 6, 2);
        g.Margin = new Thickness(0, 0, 16, 0);
        row.Children.Add(g);
        row.Children.Add(Dots(Deg(r.Temp), 6, 2));
        middle.Children.Add(row);
        middle.Children.Add(TileChrome.Body($"↑ {Deg(r.High)}   ↓ {Deg(r.Low)}", 12, FontWeights.Normal, TileChrome.Frozen(0xCC, 0xCC, 0xCC)));
        panel.Children.Add(middle);
        return panel;
    }

    // The 5-day row (mockup FC): day name, icon in dots, high.
    private static FrameworkElement Days(WeatherReport r)
    {
        var grid = new UniformGrid { Rows = 1, VerticalAlignment = VerticalAlignment.Bottom };
        foreach (var d in r.Days.Take(5))
        {
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var name = TileChrome.Label(d.Date.ToString("ddd", CultureInfo.InvariantCulture));
            name.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(name);
            var g = Dots(WeatherRules.Glyph(d.Kind, true).ToString(), 2.4, 0.9);
            g.HorizontalAlignment = HorizontalAlignment.Center;
            g.Margin = new Thickness(0, 5, 0, 5);
            col.Children.Add(g);
            var hi = TileChrome.Body(Deg(d.High), 11, FontWeights.Normal);
            hi.HorizontalAlignment = HorizontalAlignment.Center;
            col.Children.Add(hi);
            grid.Children.Add(col);
        }
        return grid;
    }

    public void Dispose() => _weather.Changed -= Fill;
}
