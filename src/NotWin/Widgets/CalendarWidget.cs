using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NotWin.Core.GoogleCalendar;
using NotWin.Core.Widgets;
using NotWin.GoogleCalendar;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// Calendar widget (spec §6.5, mockup <c>renderCal</c>). 2×2 the month (DM Serif header, S–S, today on a red circle,
/// other months muted) with a dot under every day that has a Google Calendar event (Plan 6); 2×1 the day name in red
/// over the big date, and the next event (or "No events"); 1×1 the day name top right and the big date. Not signed in:
/// plain dates, plus a "connect" hint in edit mode only. Redraws at midnight, when the next event starts or ends, and
/// when the events change; asks for a refresh when it becomes visible.
/// </summary>
public sealed class CalendarWidget : IDesktopWidget
{
    private static readonly Brush Muted = TileChrome.Frozen(0x44, 0x44, 0x44), YearBrush = TileChrome.Frozen(0x66, 0x66, 0x66);
    private const string Hint = "CONNECT GOOGLE CALENDAR IN SETTINGS";
    private readonly DispatcherTimer _midnight = new();
    private readonly GoogleCalendarService? _calendar;
    private readonly Func<bool> _editing;
    private Border? _host;
    private WidgetSize _size;

    public CalendarWidget(GoogleCalendarService? calendar = null, Func<bool>? editing = null)
    {
        _calendar = calendar;
        _editing = editing ?? (() => false);
        _midnight.Tick += (_, _) => Fill();
        if (_calendar is not null) _calendar.Changed += Fill;
    }

    private bool SignedIn => _calendar?.Status == CalendarStatus.SignedIn;
    private bool ShowHint => _calendar is not null && !SignedIn && _editing();

    public WidgetKind Kind => WidgetKind.Calendar;

    private static FontFamily Serif =>
        Application.Current?.TryFindResource("Font.Grotesk") as FontFamily ?? new FontFamily("Segoe UI");

    public FrameworkElement Build(WidgetSize size)
    {
        _size = size;
        _host = new Border();
        Fill();
        _calendar?.RefreshSoon();
        return _host;
    }

    public void SetPaused(bool paused)
    {
        if (!paused) _calendar?.RefreshSoon();
    }

    private void Fill()
    {
        if (_host is null) return;
        var now = DateTime.Now;
        var events = SignedIn ? _calendar!.Events : [];
        var next = CalendarFeed.NextEvent(events, DateTimeOffset.Now);
        try
        {
            _host.Child = _size switch
            {
                { W: 1 } => One(now),
                { H: 1 } => Wide(now, next, ShowHint),
                _ => Month(now, CalendarFeed.DaysWithEvents(events, TimeZoneInfo.Local), ShowHint),
            };
        }
        catch (Exception e)
        {
            Log.Error("calendar widget draw failed", e);
            _host.Child = One(now);
        }
        // The next redraw: midnight, or (2×1) when the shown event starts or ends, whichever comes first.
        var wait = now.Date.AddDays(1) - now;
        if (next is not null && _size is { H: 1, W: > 1 })
        {
            var at = next.Start > DateTimeOffset.Now ? next.Start : next.End;
            var untilEvent = at - DateTimeOffset.Now;
            if (untilEvent > TimeSpan.Zero && untilEvent < wait) wait = untilEvent;
        }
        _midnight.Stop();
        _midnight.Interval = wait + TimeSpan.FromSeconds(1);
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
    private static FrameworkElement Wide(DateTime now, CalEvent? next, bool hint)
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
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(next is null ? 18 : 14, 0, 0, 0) };
        if (next is not null)
        {
            // The next event (Plan 6): when, the title (two lines at most), then the month and year.
            right.Children.Add(TileChrome.Label(CalendarFeed.WhenLabel(next, DateTimeOffset.Now, TimeZoneInfo.Local), TileChrome.Red));
            var title = TileChrome.Body(next.Title, 13, FontWeights.SemiBold);
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.LineHeight = 16;
            title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            title.MaxHeight = 48; // three lines
            title.Margin = new Thickness(0, 5, 0, 0);
            right.Children.Add(title);
            var monthYear = TileChrome.Label(now.ToString("MMMM yyyy", CultureInfo.InvariantCulture));
            monthYear.Margin = new Thickness(0, 6, 0, 0);
            right.Children.Add(monthYear);
        }
        else
        {
            var none = TileChrome.Label(hint ? Hint : "No events");
            none.TextWrapping = TextWrapping.Wrap;
            right.Children.Add(none);
            var month = TileChrome.Body(now.ToString("MMMM", CultureInfo.InvariantCulture), 18, FontWeights.SemiBold);
            month.Margin = new Thickness(0, 6, 0, 0);
            right.Children.Add(month);
            var year = TileChrome.Label(now.Year.ToString(CultureInfo.InvariantCulture));
            year.Margin = new Thickness(0, 4, 0, 0);
            right.Children.Add(year);
        }
        var divider = new Border { Width = 1, Background = Muted, Margin = new Thickness(0, 10, 0, 10), HorizontalAlignment = HorizontalAlignment.Right };
        // With an event the date takes 2/5 of the width and the event 3/5, so longer titles fit.
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(next is null ? 1 : 2, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(next is null ? 1 : 3, GridUnitType.Star) },
            },
        };
        grid.Children.Add(left);
        grid.Children.Add(divider);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static FrameworkElement Month(DateTime now, IReadOnlySet<DateOnly> busy, bool hint)
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
        var first = new DateOnly(now.Year, now.Month, 1);
        var day = first.AddDays(-(int)first.DayOfWeek); // the first cell (CalendarMonth: weeks start on Sunday)
        foreach (var c in CalendarMonth.Build(now.Year, now.Month, DateOnly.FromDateTime(now)))
        {
            var date = day;
            day = day.AddDays(1);
            var text = new TextBlock
            {
                Text = c.Day.ToString(CultureInfo.InvariantCulture), FontFamily = TileChrome.Font, FontSize = 12,
                Foreground = c.IsToday ? Brushes.White : c.InMonth ? TileChrome.Text : Muted,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var cell = new Grid { Height = 30 };
            if (c.IsToday) cell.Children.Add(new Ellipse { Width = 30, Height = 30, Fill = TileChrome.Red });
            cell.Children.Add(text);
            if (busy.Contains(date))
            {
                // A day with events (Plan 6): a small dot under the number, white on today's red circle.
                cell.Children.Add(new Ellipse
                {
                    Width = 4, Height = 4, Fill = c.IsToday ? Brushes.White : c.InMonth ? TileChrome.Text : Muted,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 3),
                });
            }
            grid.Children.Add(cell);
        }
        panel.Children.Add(grid);
        if (!hint) return panel;
        // Not signed in, edit mode only (spec §6.5): where to connect, over the bottom of the month.
        var note = TileChrome.Label(Hint);
        var tag = new Border
        {
            Background = TileChrome.FrozenArgb(0xE6, 0x1A, 0x1A, 0x1A), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4), Child = note,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
        };
        return new Grid { Children = { panel, tag } };
    }

    public void Dispose()
    {
        _midnight.Stop();
        if (_calendar is not null) _calendar.Changed -= Fill;
    }
}
