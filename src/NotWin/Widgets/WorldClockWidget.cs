using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NotWin.Core.Widgets;
using NotWin.Notch;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// World clock (Plan 13, repeatable; 1×1 circle, 2×1 pill): the time in the chosen zone in dots, its offset label
/// ("GMT+5:30") and day; the 2×1 adds the difference from local time ("+4H 30M"). No city is shown. Ticks once a minute.
/// </summary>
public sealed class WorldClockWidget : IDesktopWidget
{
    private readonly Func<DateTime> _utcNow;
    private readonly TimeZoneInfo _local;
    private readonly MinuteTicker _minute;
    private TimeZoneInfo _zone = TimeZoneInfo.Utc;
    private WidgetSize _size;
    private Border? _host;
    private bool _paused;

    /// <param name="local">This PC's zone (tests pass a fixed one).</param>
    public WorldClockWidget(Func<DateTime> utcNow, TimeZoneInfo? local = null)
    {
        _utcNow = utcNow;
        _local = local ?? TimeZoneInfo.Local;
        _minute = new MinuteTicker(Fill);
    }

    public WidgetKind Kind => WidgetKind.WorldClock;

    public void Configure(WidgetConfig? config)
    {
        var id = WidgetChoices.Normalize(WidgetKind.WorldClock, config?.Choice) ?? "UTC";
        try { _zone = TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { _zone = TimeZoneInfo.Utc; }
    }

    public FrameworkElement Build(WidgetSize size)
    {
        _size = size;
        _host = new Border();
        Fill();
        if (!_paused) _minute.Start();
        return _host;
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused) _minute.Stop();
        else
        {
            Fill();
            _minute.Start();
        }
    }

    private void Fill()
    {
        if (_host is null) return;
        var utc = DateTime.SpecifyKind(_utcNow(), DateTimeKind.Utc);
        var there = TimeZoneInfo.ConvertTimeFromUtc(utc, _zone);
        var offset = ZoneLabels.Offset(_zone, utc);
        var time = there.ToString("HH:mm", CultureInfo.InvariantCulture);
        var label = ZoneLabels.GmtLabel(offset);
        if (_size.W == 1)
        {
            _host.Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new DotMatrix { Text = time, Dot = 2.4, Gap = 1, Fill = TileChrome.Text, HorizontalAlignment = HorizontalAlignment.Center },
                    Centered(TileChrome.Label($"{label} · {Day(there, "ddd")}"), 8),
                },
            };
            return;
        }
        var right = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0),
            Children =
            {
                TileChrome.Label(Day(there, "dddd"), TileChrome.Text),
                Spaced(TileChrome.Label(label)),
                Spaced(TileChrome.Label(ZoneLabels.Difference(ZoneLabels.Offset(_local, utc), offset))),
            },
        };
        _host.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0),
            Children = { new DotMatrix { Text = time, Dot = 4.2, Gap = 1.5, Fill = TileChrome.Text, VerticalAlignment = VerticalAlignment.Center }, right },
        };
    }

    private static string Day(DateTime t, string format) => t.ToString(format, CultureInfo.InvariantCulture).ToUpperInvariant();

    private static TextBlock Centered(TextBlock t, double top)
    {
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, top, 0, 0);
        return t;
    }

    private static TextBlock Spaced(TextBlock t)
    {
        t.Margin = new Thickness(0, 3, 0, 0);
        return t;
    }

    public void Dispose() => _minute.Dispose();
}
