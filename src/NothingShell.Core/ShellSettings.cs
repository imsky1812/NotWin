namespace NothingShell.Core;

public sealed record ShellSettings
{
    public bool KeepDockVisible { get; init; } = false;
    public bool StartWithWindows { get; init; } = true;
    public string WidgetScale { get; init; } = "M";
    public IReadOnlyList<PinnedApp> PinnedApps { get; init; } = [];
    public bool PinsImported { get; init; } = false;
}
