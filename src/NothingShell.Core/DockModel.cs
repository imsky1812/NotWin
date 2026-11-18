namespace NothingShell.Core;

public sealed record PinnedApp(string AppId, string DisplayName, string LaunchPath);

/// <summary>A top-level window the dock tracks. <paramref name="AppId"/> is the AUMID, or the lower-case exe path.</summary>
public sealed record AppWindow(nint Handle, string AppId, string Title, string ExePath, bool IsForeground);

public sealed record DockItem(
    string AppId, string DisplayName, string? LaunchPath, bool Pinned, IReadOnlyList<AppWindow> Windows)
{
    public bool Running => Windows.Count > 0;
    public bool Focused => Windows.Any(w => w.IsForeground);
}

public enum DockClickAction { Launch, Activate, Minimize, ShowWindowList }

public static class DockItemsBuilder
{
    /// <summary>
    /// Pinned apps first (pinned order), then running unpinned apps in order of first appearance.
    /// Windows with an empty title are skipped. App ids compare case-insensitively.
    /// </summary>
    public static IReadOnlyList<DockItem> Build(IReadOnlyList<PinnedApp> pinned, IReadOnlyList<AppWindow> windows)
    {
        var byApp = new Dictionary<string, List<AppWindow>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var w in windows)
        {
            if (string.IsNullOrEmpty(w.Title)) continue;
            if (!byApp.TryGetValue(w.AppId, out var list))
            {
                byApp[w.AppId] = list = [];
                order.Add(w.AppId);
            }
            list.Add(w);
        }

        var items = new List<DockItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in pinned)
        {
            if (!seen.Add(p.AppId)) continue;
            items.Add(new DockItem(p.AppId, p.DisplayName, p.LaunchPath, true,
                byApp.TryGetValue(p.AppId, out var ws) ? ws : []));
        }

        foreach (var id in order)
        {
            if (!seen.Add(id)) continue;
            var ws = byApp[id];
            items.Add(new DockItem(id, DisplayNameFor(ws[0]), ws[0].ExePath, false, ws));
        }

        return items;
    }

    public static DockClickAction DecideClick(DockItem item)
    {
        if (!item.Running) return DockClickAction.Launch;
        if (item.Windows.Count > 1) return DockClickAction.ShowWindowList;
        return item.Focused ? DockClickAction.Minimize : DockClickAction.Activate;
    }

    private static string DisplayNameFor(AppWindow w)
    {
        var name = Path.GetFileNameWithoutExtension(w.ExePath);
        return string.IsNullOrEmpty(name) ? w.AppId : name;
    }
}
