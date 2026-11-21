namespace NotWin.Core.Widgets;

/// <summary>A day in the month grid: its number, whether it belongs to the shown month, and whether it is today.</summary>
public sealed record CalendarCell(int Day, bool InMonth, bool IsToday);

/// <summary>
/// The calendar widget's month (mockup <c>renderCal</c>): weeks start on Sunday (S M T W T F S); days of the previous
/// and next month fill the first and last week (muted); 35 or 42 cells.
/// </summary>
public static class CalendarMonth
{
    public static IReadOnlyList<CalendarCell> Build(int year, int month, DateOnly today)
    {
        var first = new DateOnly(year, month, 1);
        var lead = (int)first.DayOfWeek; // Sunday = 0
        var days = DateTime.DaysInMonth(year, month);
        var prev = first.AddMonths(-1);
        var prevDays = DateTime.DaysInMonth(prev.Year, prev.Month);
        var cells = new List<CalendarCell>(42);
        for (var i = 0; i < lead; i++) cells.Add(new CalendarCell(prevDays - lead + 1 + i, false, false));
        for (var d = 1; d <= days; d++) cells.Add(new CalendarCell(d, true, today == new DateOnly(year, month, d)));
        for (var d = 1; cells.Count % 7 != 0; d++) cells.Add(new CalendarCell(d, false, false));
        return cells;
    }
}
