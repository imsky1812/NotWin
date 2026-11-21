using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NothingShell.Core.GoogleCalendar;
using NothingShell.Core.Widgets;
using NothingShell.GoogleCalendar;
using NothingShell.Native;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Widgets;

/// <summary>
/// "Up next" (Plan 6b, user 2026-11-21): only the user's own plans from Google Calendar (meetings, things they added,
/// trips and bookings Gmail put in the calendar), never holidays or birthdays. 2×2 the next 4, 2×1 the next one large,
/// 4×2 the next 6 in two columns. Each row: when (red when soon), the title, the place. A click opens the event in
/// Google Calendar in the browser. Redraws every minute while visible (the countdown). Titles, places and links are
/// never logged.
/// </summary>
public sealed class AgendaWidget : IDesktopWidget
{
    private readonly GoogleCalendarService? _calendar;
    private readonly DispatcherTimer _minute = new() { Interval = TimeSpan.FromMinutes(1) };
    private Border? _host;
    private WidgetSize _size;
    private bool _paused;

    public AgendaWidget(GoogleCalendarService? calendar)
    {
        _calendar = calendar;
        _minute.Tick += (_, _) => Fill();
        if (_calendar is not null) _calendar.Changed += Fill;
    }

    public WidgetKind Kind => WidgetKind.Agenda;

    public FrameworkElement Build(WidgetSize size)
    {
        _size = size;
        _host = new Border();
        Fill();
        if (!_paused) _minute.Start();
        _calendar?.RefreshSoon();
        return _host;
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused) { _minute.Stop(); return; }
        _minute.Start();
        Fill();
        _calendar?.RefreshSoon();
    }

    private void Fill()
    {
        if (_host is null) return;
        try { _host.Child = Draw(); }
        catch (Exception e)
        {
            Log.Error("up next widget draw failed", e);
            _host.Child = Message("UP NEXT", "");
        }
    }

    private FrameworkElement Draw()
    {
        if (_calendar?.Status != CalendarStatus.SignedIn)
            return Message("UP NEXT", "CONNECT GOOGLE CALENDAR IN SETTINGS");
        var now = DateTimeOffset.Now;
        var max = _size switch { { H: 1 } => 1, { W: >= 4 } => 6, _ => 4 };
        var events = CalendarFeed.Upcoming(_calendar.Events, now, max);
        if (events.Count == 0) return Message("UP NEXT", "NOTHING PLANNED");
        if (_size.H == 1) return Big(events[0], now);

        var panel = new DockPanel();
        var head = TileChrome.Label("UP NEXT");
        head.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(head, System.Windows.Controls.Dock.Top);
        panel.Children.Add(head);
        var columns = _size.W >= 4 ? 2 : 1;
        var grid = new Grid();
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        var perColumn = (int)Math.Ceiling(events.Count / (double)columns);
        for (var c = 0; c < columns; c++)
        {
            var list = new StackPanel { Margin = new Thickness(c == 0 ? 0 : 10, 0, c == columns - 1 ? 0 : 10, 0) };
            foreach (var e in events.Skip(c * perColumn).Take(perColumn)) list.Children.Add(Row(e, now));
            Grid.SetColumn(list, c);
            grid.Children.Add(list);
        }
        panel.Children.Add(grid);
        return panel;
    }

    // One event in the list: when, then the title and the place on one line each.
    private static FrameworkElement Row(CalEvent e, DateTimeOffset now)
    {
        var (when, soon) = CalendarFeed.Countdown(e, now, TimeZoneInfo.Local);
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
        var label = TileChrome.Label(when, soon ? TileChrome.Red : null);
        label.FontSize = 9;
        stack.Children.Add(label);
        var title = TileChrome.Body(e.Title, 13, FontWeights.SemiBold);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(title);
        if (e.Location is { } place)
        {
            var where = TileChrome.Body(place, 11, FontWeights.Normal, TileChrome.Dim);
            where.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(where);
        }
        return Clickable(stack, e);
    }

    // 2×1: the next event large — when, the title on up to two lines, the place.
    private static FrameworkElement Big(CalEvent e, DateTimeOffset now)
    {
        var (when, soon) = CalendarFeed.Countdown(e, now, TimeZoneInfo.Local);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(TileChrome.Label("UP NEXT · " + when, soon ? TileChrome.Red : null));
        var title = TileChrome.Body(e.Title, 18, FontWeights.SemiBold);
        title.TextWrapping = TextWrapping.Wrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.MaxHeight = 50;
        title.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(title);
        if (e.Location is { } place)
        {
            var where = TileChrome.Body(place, 12, FontWeights.Normal, TileChrome.Dim);
            where.TextTrimming = TextTrimming.CharacterEllipsis;
            where.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(where);
        }
        return Clickable(stack, e);
    }

    private static FrameworkElement Message(string head, string text)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(TileChrome.Label(head));
        if (text.Length > 0)
        {
            var body = TileChrome.Body(text, 12, FontWeights.SemiBold);
            body.TextWrapping = TextWrapping.Wrap;
            body.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(body);
        }
        return stack;
    }

    // A click opens the event in Google Calendar (https Google Calendar links only), through Explorer so the browser
    // is not elevated.
    private static FrameworkElement Clickable(FrameworkElement content, CalEvent e)
    {
        if (!CalendarFeed.IsSafeLink(e.Link)) return content;
        var link = e.Link!;
        var host = new Border { Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand, Child = content };
        host.MouseLeftButtonUp += (_, args) =>
        {
            args.Handled = true;
            if (!UnelevatedShell.Open(link, null, (m, _) => Log.Info("up next: " + m))) Log.Info("up next: the event did not open");
        };
        return host;
    }

    public void Dispose()
    {
        _minute.Stop();
        if (_calendar is not null) _calendar.Changed -= Fill;
    }
}
