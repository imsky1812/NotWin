namespace NotWin.Core.Widgets;

/// <summary>
/// The dot glyph that stands for each widget kind in the ＋ Add list (Plan 14, user 2026-11-27: the clock family all
/// shared one cup). Names resolve through <see cref="DotArt"/>.
/// </summary>
public static class WidgetIcons
{
    public static string For(WidgetKind kind) => kind switch
    {
        WidgetKind.Photo => "camera",
        WidgetKind.Folder => "folder",
        WidgetKind.Snake or WidgetKind.TicTacToe => "gamepad",
        WidgetKind.Media => "note",
        WidgetKind.Notes => "book",
        WidgetKind.Weather => "cloud",
        WidgetKind.Clock => "digits",
        WidgetKind.AnalogClock => "clock",
        WidgetKind.StackedClock => "stack",
        WidgetKind.WorldClock => "globe",
        WidgetKind.Timer => "timer",
        WidgetKind.Stopwatch => "stopwatch",
        WidgetKind.ScreenTime => "hourglass",
        WidgetKind.Alarm => "alarm",
        WidgetKind.Calendar or WidgetKind.Agenda => "calendar",
        WidgetKind.Battery => "battery",
        WidgetKind.Toggle => "switch",
        WidgetKind.SystemMeters => "bars",
        WidgetKind.SunCompass => "sun",
        _ => "star",
    };
}
