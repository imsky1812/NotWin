namespace NotWin.Core.Widgets;

/// <summary>
/// What a toggle, world clock or analogue clock shows (Plan 13, <see cref="WidgetConfig.Choice"/>): a toggle id, a
/// Windows time-zone id or a clock face. Chosen in edit mode from a list; unknown values fall back to the default.
/// </summary>
public static class WidgetChoices
{
    public static IReadOnlyList<string> Toggles { get; } = ["wifi", "bluetooth", "dark", "awake", "mic", "camera"];

    public static IReadOnlyList<string> Faces { get; } = ["light", "dark"];

    /// <summary>Tic-tac-toe opponents: the shell, or a second player on the same PC (user 2026-11-27).</summary>
    public static IReadOnlyList<string> GameModes { get; } = ["ai", "two"];

    /// <summary>One Windows zone per common offset, UTC−10 to UTC+13 (half and three-quarter hours included).</summary>
    public static IReadOnlyList<string> Zones { get; } =
    [
        "Hawaiian Standard Time", "Alaskan Standard Time", "Pacific Standard Time", "Mountain Standard Time", "Central Standard Time",
        "Eastern Standard Time", "SA Eastern Standard Time", "UTC-02", "Azores Standard Time", "UTC", "GMT Standard Time",
        "W. Europe Standard Time", "E. Europe Standard Time", "Arab Standard Time", "Arabian Standard Time", "Pakistan Standard Time",
        "India Standard Time", "Nepal Standard Time", "Bangladesh Standard Time", "SE Asia Standard Time", "China Standard Time",
        "Tokyo Standard Time", "Cen. Australia Standard Time", "AUS Eastern Standard Time", "New Zealand Standard Time", "Tonga Standard Time",
    ];

    /// <summary>True for the kinds whose config carries a choice.</summary>
    public static bool HasChoice(WidgetKind kind) => kind is WidgetKind.Toggle or WidgetKind.WorldClock or WidgetKind.AnalogClock or WidgetKind.TicTacToe;

    /// <summary>The ids a kind can choose from (empty for kinds without a choice).</summary>
    public static IReadOnlyList<string> Options(WidgetKind kind) => kind switch
    {
        WidgetKind.Toggle => Toggles,
        WidgetKind.WorldClock => Zones,
        WidgetKind.AnalogClock => Faces,
        WidgetKind.TicTacToe => GameModes,
        _ => [],
    };

    /// <summary>
    /// A new widget's choice: the first toggle not already on the desktop (all used: Wi-Fi), UTC for a world clock, the
    /// light face for the analogue clock; null for other kinds.
    /// </summary>
    public static string? Default(WidgetKind kind, IEnumerable<WidgetPlacement> onDesktop) => kind switch
    {
        WidgetKind.Toggle => FirstUnused(onDesktop),
        WidgetKind.WorldClock => "UTC",
        WidgetKind.AnalogClock => "light",
        WidgetKind.TicTacToe => "ai",
        _ => null,
    };

    /// <summary>A saved choice, or the kind's default when it is missing or unknown; other kinds are untouched.</summary>
    public static string? Normalize(WidgetKind kind, string? choice)
    {
        if (!HasChoice(kind)) return choice;
        return choice is not null && Options(kind).Contains(choice) ? choice : Default(kind, []);
    }

    /// <summary>The config ＋ Add gives a new widget (null for kinds without a choice).</summary>
    public static WidgetConfig? ConfigFor(WidgetKind kind, IEnumerable<WidgetPlacement> onDesktop) =>
        Default(kind, onDesktop) is { } choice ? new WidgetConfig(Choice: choice) : null;

    private static string FirstUnused(IEnumerable<WidgetPlacement> onDesktop)
    {
        var used = onDesktop.Where(p => p.Kind == WidgetKind.Toggle).Select(p => p.Config?.Choice).ToHashSet();
        return Toggles.FirstOrDefault(t => !used.Contains(t)) ?? Toggles[0];
    }
}
