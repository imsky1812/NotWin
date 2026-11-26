using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NotWin.Core.Forecast;
using NotWin.Core.Widgets;
using NotWin.Notch;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// Sun compass (Plan 13, 1×1 / 2×2 circle): a compass face that points at the sun. A ring of dots, "N" in red and tiny
/// E / S / W, and a dot arrow turned to the sun's azimuth; dim while the sun is down. The 2×2 adds sunrise, sunset and
/// the sun's elevation (or "BELOW HORIZON"). The place is the weather setting's coordinates; the city is never shown.
/// Redraws every 5 minutes and when the weather place changes.
/// </summary>
public sealed class SunCompassWidget : IDesktopWidget
{
    private readonly Func<WeatherPlace?> _place;
    private readonly Action<Action> _unsubscribe;
    private readonly Func<DateTime> _utcNow;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(5) };
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private WidgetSize _size;
    private Border? _host;
    private bool _paused, _disposed;

    public SunCompassWidget(Func<WeatherPlace?> place, Action<Action> subscribe, Action<Action> unsubscribe, Func<DateTime> utcNow)
    {
        _place = place;
        _unsubscribe = unsubscribe;
        _utcNow = utcNow;
        _timer.Tick += (_, _) => Fill();
        subscribe(OnPlaceChanged);
    }

    public WidgetKind Kind => WidgetKind.SunCompass;

    internal double ArrowAngle { get; private set; }
    internal bool SunDown { get; private set; }
    internal bool Ticking => _timer.IsEnabled;

    public FrameworkElement Build(WidgetSize size)
    {
        _size = size;
        _host = new Border();
        Fill();
        if (!_paused && !_disposed) _timer.Start();
        return _host;
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused) _timer.Stop();
        else if (!_disposed)
        {
            Fill();
            _timer.Start();
        }
    }

    private void OnPlaceChanged()
    {
        if (_dispatcher.CheckAccess()) Fill();
        else _dispatcher.BeginInvoke(Fill);
    }

    private void Fill()
    {
        if (_host is null || _disposed) return;
        var big = _size.W == 2;
        if (_place() is not { } place)
        {
            var hint = TileChrome.Label("Set a city in Settings");
            hint.TextWrapping = TextWrapping.Wrap;
            hint.TextAlignment = TextAlignment.Center;
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.MaxWidth = big ? 180 : 90;
            _host.Child = hint;
            return;
        }
        var utc = DateTime.SpecifyKind(_utcNow(), DateTimeKind.Utc);
        var (azimuth, elevation) = SolarPosition.Position(utc, place.Lat, place.Lon);
        ArrowAngle = azimuth;
        SunDown = elevation < -0.833;
        var face = Face(big ? 168 : 112, azimuth, SunDown);
        if (!big)
        {
            _host.Child = face;
            return;
        }
        var lines = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        var times = SolarPosition.SunTimes(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local)), place.Lat, place.Lon, TimeZoneInfo.Local);
        if (times is { } t)
        {
            lines.Children.Add(Center(TileChrome.Label($"Sunrise {Hm(t.Rise)}")));
            lines.Children.Add(Center(TileChrome.Label($"Sunset {Hm(t.Set)}")));
        }
        var sun = SunDown ? "Below horizon" : $"Sun {Math.Round(elevation).ToString(CultureInfo.InvariantCulture)}°";
        lines.Children.Add(Center(TileChrome.Label(sun, SunDown ? null : TileChrome.Text)));
        var grid = new Grid();
        grid.Children.Add(face);
        lines.VerticalAlignment = VerticalAlignment.Bottom;
        lines.Margin = new Thickness(0, 0, 0, 22);
        face.VerticalAlignment = VerticalAlignment.Top;
        face.Margin = new Thickness(0, 10, 0, 0);
        grid.Children.Add(lines);
        _host.Child = grid;
    }

    private static FrameworkElement Face(double diameter, double azimuth, bool down)
    {
        var ring = new DotRingView
        {
            Count = 36, Radius = diameter / 2 - 4, Dot = diameter > 150 ? 3.5 : 2.4, Lit = 0,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var dot = diameter > 150 ? 4.2 : 2.6;
        var arrow = new DotArtView
        {
            Rows = DotArt.Rows("arrow"), Dot = dot, Gap = dot * 0.35, Fill = TileChrome.Text, Opacity = down ? 0.3 : 1,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(azimuth),
        };
        var size = diameter > 150 ? 13.0 : 9.0;
        var inset = diameter > 150 ? 18.0 : 11.0;
        TextBlock Letter(string s, Brush b, HorizontalAlignment h, VerticalAlignment v, Thickness m) => new()
        {
            Text = s, FontFamily = TileChrome.Mono, FontSize = size, Foreground = b, HorizontalAlignment = h, VerticalAlignment = v, Margin = m,
        };
        var dim = WidgetCard.Dim(Surface.Dark);
        return new Grid
        {
            Width = diameter, Height = diameter,
            Children =
            {
                ring,
                Letter("N", TileChrome.Red, HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, inset, 0, 0)),
                Letter("E", dim, HorizontalAlignment.Right, VerticalAlignment.Center, new Thickness(0, 0, inset, 0)),
                Letter("S", dim, HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, inset)),
                Letter("W", dim, HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(inset, 0, 0, 0)),
                arrow,
            },
        };
    }

    private static TextBlock Center(TextBlock t)
    {
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 2, 0, 0);
        return t;
    }

    private static string Hm(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _unsubscribe(OnPlaceChanged);
    }
}
