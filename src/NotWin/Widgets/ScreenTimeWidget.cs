using System.Windows;
using System.Windows.Controls;
using NotWin.Core.Widgets;
using NotWin.Notch;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// Screen time (Plan 13, 2×1 / 2×2): today's time at the PC in dots ("6H20"). 2×1 adds "TODAY" and the 7-day average;
/// 2×2 adds a week of dot columns (one dot per hour, up to 12), today's column red, day letters underneath. No app
/// names. Redraws when the tracker counts another minute.
/// </summary>
public sealed class ScreenTimeWidget : IDesktopWidget
{
    private readonly ScreenTimeTracker _tracker;
    private WidgetSize _size;
    private Border? _host;

    public ScreenTimeWidget(ScreenTimeTracker tracker)
    {
        _tracker = tracker;
        _tracker.Changed += Fill;
    }

    public WidgetKind Kind => WidgetKind.ScreenTime;

    public FrameworkElement Build(WidgetSize size)
    {
        _size = size;
        _host = new Border();
        Fill();
        return _host;
    }

    private void Fill()
    {
        if (_host is null) return;
        var now = _tracker.Now;
        var log = _tracker.Log;
        var today = ScreenTimeLog.Format(log.Today(now));
        if (_size.H == 1)
        {
            _host.Child = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
                Children =
                {
                    WidgetTheme.Label("Today"),
                    new DotMatrix { Text = today, Dot = 5, Gap = 1.8, Fill = WidgetTheme.Text, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 8) },
                    WidgetTheme.Label("Avg " + ScreenTimeLog.Format(log.Average7(now))),
                },
            };
            return;
        }
        var week = log.Week(now);
        var letters = new UniformGrid7();
        foreach (var (day, _) in week)
            letters.Add(WidgetTheme.Label(day.DayOfWeek.ToString()[..1]));
        _host.Child = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new DotMatrix { Text = today, Dot = 4, Gap = 1.5, Fill = WidgetTheme.Text, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Width = 10 },
                        WidgetTheme.Label("Today"),
                    },
                },
                new DotChartView
                {
                    ColumnCount = 7, Rows = 12, Dot = 7, Gap = 3, ColumnGap = 25, AccentColumn = 6, On = WidgetTheme.Text, Off = WidgetTheme.Unlit, HorizontalAlignment = HorizontalAlignment.Left,
                    Columns = week.Select(d => (int)Math.Round(d.Minutes / 60.0)).ToList(), Margin = new Thickness(0, 14, 0, 6),
                },
                letters.Panel,
            },
        };
    }

    // Day letters under the chart's columns: one 32-DIP cell per column (dot 7 + column gap 25).
    private sealed class UniformGrid7
    {
        public StackPanel Panel { get; } = new() { Orientation = Orientation.Horizontal };

        public void Add(TextBlock t)
        {
            t.Width = 32;
            t.Margin = new Thickness(-1, 0, 0, 0);
            Panel.Children.Add(t);
        }
    }

    public void Dispose() => _tracker.Changed -= Fill;
}
