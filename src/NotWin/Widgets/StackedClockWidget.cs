using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NotWin.Core.Widgets;
using NotWin.Notch;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>Stacked clock (Plan 13, 1×2 tall pill / 1×1 circle): hours above minutes in large dots. Ticks once a minute.</summary>
public sealed class StackedClockWidget : IDesktopWidget
{
    private readonly Func<DateTime> _now;
    private readonly MinuteTicker _minute;
    private DotMatrix? _hours, _minutes;
    private bool _paused;

    public StackedClockWidget(Func<DateTime> now)
    {
        _now = now;
        _minute = new MinuteTicker(Tick);
    }

    public WidgetKind Kind => WidgetKind.StackedClock;

    public FrameworkElement Build(WidgetSize size)
    {
        var (dot, gap, space) = size.H == 2 ? (6.5, 2.3, 18.0) : (3.6, 1.3, 8.0);
        _hours = new DotMatrix { Dot = dot, Gap = gap, Fill = TileChrome.Text, HorizontalAlignment = HorizontalAlignment.Center };
        _minutes = new DotMatrix { Dot = dot, Gap = gap, Fill = TileChrome.Text, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, space, 0, 0) };
        Tick();
        if (!_paused) _minute.Start();
        return new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _hours, _minutes } };
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused) _minute.Stop();
        else
        {
            Tick();
            _minute.Start();
        }
    }

    private void Tick()
    {
        var now = _now();
        if (_hours is not null) _hours.Text = now.ToString("HH", CultureInfo.InvariantCulture);
        if (_minutes is not null) _minutes.Text = now.ToString("mm", CultureInfo.InvariantCulture);
    }

    public void Dispose() => _minute.Dispose();
}
