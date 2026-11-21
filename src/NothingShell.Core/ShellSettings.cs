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
}
