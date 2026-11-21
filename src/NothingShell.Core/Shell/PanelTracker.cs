namespace NothingShell.Core.Shell;

/// <summary>
/// Which surfaces currently hold the dock up (spec §6.1: it hides "unless a panel is open"). Each surface sets its
/// own flag by name, so closing one (a dock popup) never clears another's hold (the quick-settings panel).
/// </summary>
public sealed class PanelTracker
{
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);

    public void Set(string source, bool open)
    {
        if (open) _open.Add(source);
        else _open.Remove(source);
    }

    public bool AnyOpen => _open.Count > 0;
}
