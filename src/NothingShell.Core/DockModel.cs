namespace NothingShell.Core;

/// <summary>
/// A pinned app. <paramref name="TargetExe"/> (lower-case exe path, optional) is a fallback match key for when a running
/// window's AppId differs from the pin's (e.g. the shortcut has no AUMID but the window does, or vice versa).
/// </summary>
public sealed record PinnedApp(string AppId, string DisplayName, string LaunchPath, string? TargetExe = null);

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
        var pinnedIds = new HashSet<string>(pinned.Select(p => p.AppId), StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // running AppIds absorbed by a pin
        foreach (var p in pinned)
        {
            if (!seen.Add(p.AppId)) continue;
            List<AppWindow>? ws = null;
            if (byApp.TryGetValue(p.AppId, out var exact)) { ws = exact; claimed.Add(p.AppId); }
            else if (!string.IsNullOrEmpty(p.TargetExe))
            {
                // Fallback: an unpinned running app launched from the same exe belongs to this pin.
                foreach (var id in order)
                {
                    if (pinnedIds.Contains(id) || claimed.Contains(id)) continue;
                    var first = byApp[id][0];
                    if (!string.Equals(id, first.ExePath, StringComparison.OrdinalIgnoreCase)) continue; // has its own AUMID
                    if (!string.Equals(first.ExePath, p.TargetExe, StringComparison.OrdinalIgnoreCase)) continue;
                    ws = byApp[id]; claimed.Add(id);
                    break;
                }
            }
            items.Add(new DockItem(p.AppId, p.DisplayName, p.LaunchPath, true, ws ?? []));
        }

        foreach (var id in order)
        {
            if (claimed.Contains(id) || !seen.Add(id)) continue;
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
