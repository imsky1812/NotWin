using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NothingShell.Core.Widgets;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Widgets;

/// <summary>
/// Calendar widget (spec §6.5, mockup <c>renderCal</c>), dates only — Google sign-in and events are Plan 6 (user
/// decision 2026-11-19). 2×2 the month (DM Serif header, S–S, today on a red circle, other months muted); 2×1 the day
/// name in red over the big date, plus "NO EVENTS"; 1×1 the day name top right and the big date. Redraws at midnight.
/// </summary>
public sealed class CalendarWidget : IDesktopWidget
{
    private static readonly Brush Muted = TileChrome.Frozen(0x44, 0x44, 0x44), YearBrush = TileChrome.Frozen(0x66, 0x66, 0x66);
    private readonly DispatcherTimer _midnight = new();
    private Border? _host;
    private WidgetSize _size;

    public CalendarWidget() => _midnight.Tick += (_, _) => Fill();

    public WidgetKind Kind => WidgetKind.Calendar;

    private static FontFamily Serif =>
        Application.Current?.TryFindResource("Font.Serif") as FontFamily ?? new FontFamily("Georgia");

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
        var now = DateTime.Now;
        _host.Child = _size switch
        {
            { W: 1 } => One(now),
            { H: 1 } => Wide(now),
            _ => Month(now),
        };
        _midnight.Stop();
        _midnight.Interval = now.Date.AddDays(1) - now + TimeSpan.FromSeconds(1);
        _midnight.Start();
    }

    private static TextBlock SerifText(string text, double size, Brush? brush = null) =>
        new() { Text = text, FontFamily = Serif, FontSize = size, Foreground = brush ?? TileChrome.Text };

    private static FrameworkElement One(DateTime now)
    {
        var day = SerifText(now.ToString("ddd", CultureInfo.InvariantCulture), 18, TileChrome.Red);
        day.HorizontalAlignment = HorizontalAlignment.Right;
        day.VerticalAlignment = VerticalAlignment.Top;
        var date = SerifText(now.Day.ToString(CultureInfo.InvariantCulture), 76);
        date.HorizontalAlignment = HorizontalAlignment.Center;
        date.VerticalAlignment = VerticalAlignment.Bottom;
        date.LineHeight = 76;
        date.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        return new Grid { Children = { day, date } };
    }

    // 2×1 (user, 2026-11-21: "pace the content better"): two equal halves — the day and big date centred on the left,
    // a thin divider, then "No events", the month and the year centred vertically on the right.
    private static FrameworkElement Wide(DateTime now)
    {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var day = SerifText(now.ToString("ddd", CultureInfo.InvariantCulture), 18, TileChrome.Red);
        day.HorizontalAlignment = HorizontalAlignment.Center;
        left.Children.Add(day);
        var date = SerifText(now.Day.ToString(CultureInfo.InvariantCulture), 64);
        date.HorizontalAlignment = HorizontalAlignment.Center;
        date.LineHeight = 64;
        date.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        left.Children.Add(date);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        right.Children.Add(TileChrome.Label("No events"));
        var month = TileChrome.Body(now.ToString("MMMM", CultureInfo.InvariantCulture), 18, FontWeights.SemiBold);
        month.Margin = new Thickness(0, 6, 0, 0);
        right.Children.Add(month);
        var year = TileChrome.Label(now.Year.ToString(CultureInfo.InvariantCulture));
        year.Margin = new Thickness(0, 4, 0, 0);
        right.Children.Add(year);
        var divider = new Border { Width = 1, Background = Muted, Margin = new Thickness(0, 10, 0, 10), HorizontalAlignment = HorizontalAlignment.Right };
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
        grid.Children.Add(left);
        grid.Children.Add(divider);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static FrameworkElement Month(DateTime now)
    {
        var panel = new DockPanel();
        var head = new TextBlock { FontFamily = Serif, FontSize = 20, Foreground = TileChrome.Text };
        head.Inlines.Add(new System.Windows.Documents.Run(now.ToString("MMMM", CultureInfo.InvariantCulture) + " "));
        head.Inlines.Add(new System.Windows.Documents.Run(now.Year.ToString(CultureInfo.InvariantCulture)) { Foreground = YearBrush });
        DockPanel.SetDock(head, System.Windows.Controls.Dock.Top);
        panel.Children.Add(head);
        var grid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var d in new[] { "S", "M", "T", "W", "T", "F", "S" })
        {
            var l = TileChrome.Label(d);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.Height = 18;
            grid.Children.Add(l);
        }
        foreach (var c in CalendarMonth.Build(now.Year, now.Month, DateOnly.FromDateTime(now)))
        {
            var text = new TextBlock
            {
                Text = c.Day.ToString(CultureInfo.InvariantCulture), FontFamily = TileChrome.Font, FontSize = 12,
                Foreground = c.IsToday ? Brushes.White : c.InMonth ? TileChrome.Text : Muted,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var cell = new Grid { Height = 30 };
            if (c.IsToday) cell.Children.Add(new Ellipse { Width = 30, Height = 30, Fill = TileChrome.Red });
            cell.Children.Add(text);
            grid.Children.Add(cell);
        }
        panel.Children.Add(grid);
        return panel;
    }

    public void Dispose() => _midnight.Stop();
}
