namespace NothingShell.Core;

public sealed record ShellSettings
{
    public bool KeepDockVisible { get; init; } = false;
    public bool StartWithWindows { get; init; } = true;
    public string WidgetScale { get; init; } = "M";
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

    /// <summary>Where the desktop widgets are and their sizes (null = the default layout). Sanitized on use.</summary>
    public Widgets.DesktopLayout? Widgets { get; init; }

    /// <summary>The snake widget's best score.</summary>
    public int SnakeBest { get; init; }
}
