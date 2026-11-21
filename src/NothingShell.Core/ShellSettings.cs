namespace NothingShell.Core;

public sealed record ShellSettings
{
    public bool KeepDockVisible { get; init; } = false;
    public bool StartWithWindows { get; init; } = true;
    public string WidgetScale { get; init; } = "M";

    /// <summary>Spec §5 global widget scale: S 50 %, M 65 % (default), L 80 %.</summary>
    public static double ScaleFactor(string? scale) => scale switch { "S" => 0.5, "L" => 0.8, _ => 0.65 };
    public IReadOnlyList<PinnedApp> PinnedApps { get; init; } = [];
    public bool PinsImported { get; init; } = false;

    /// <summary>The panel's one-time "turn off Windows' pop-ups" tip was dismissed ("Got it").</summary>
    public bool BannerTipDismissed { get; init; } = false;

    /// <summary>The notch dashboard's pages and removed tiles (null = the default layout). Sanitized on use.</summary>
    public Notch.DashboardLayout? NotchLayout { get; init; }

    /// <summary>The timer's length in minutes (spec §6.5 default 25), saved when − / + change it while idle.</summary>
    public int TimerMinutes { get; init; } = 25;

    /// <summary>The weather tile's place (spec §6.5: a city the user types in Settings); null until set.</summary>
    public Forecast.WeatherPlace? Weather { get; init; }

    /// <summary>
    /// Plan 5 layout (one widget per kind, no ids). Read once to convert into <see cref="WidgetList"/> and never written
    /// again, so an older build reading this file still finds its layout.
    /// </summary>
    public Widgets.DesktopLayout? Widgets { get; init; }

    /// <summary>The desktop widget instances (Plan 5b): null = not converted yet. Sanitized on use.</summary>
    public Widgets.DesktopLayout? WidgetList { get; init; }

    /// <summary>Plan 7: live window previews when hovering a running app in the dock.</summary>
    public bool WindowPreviews { get; init; } = true;

    /// <summary>The snake widget's best score.</summary>
    public int SnakeBest { get; init; }

    /// <summary>The tic-tac-toe widget's score (Plan 9).</summary>
    public Widgets.GameScore TicTacToeScore { get; init; } = new();
}
