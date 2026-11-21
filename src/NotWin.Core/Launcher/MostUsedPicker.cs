namespace NotWin.Core.Launcher;

/// <summary>
/// The launcher's empty-state "Most used" apps (spec §6.2): ranked by launches from NotWin; until there is
/// enough history, filled with the most recently used apps from Windows' UserAssist, then alphabetically.
/// Only apps that exist in the current index are returned.
/// </summary>
public static class MostUsedPicker
{
    public static IReadOnlyList<LaunchItem> Pick(IReadOnlyList<LaunchItem> apps, IReadOnlyDictionary<string, LaunchUsage> usage,
        IReadOnlyList<UserAssistEntry> recent, int count = 6)
    {
        var byId = new Dictionary<string, LaunchItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in apps) byId.TryAdd(a.Id, a);

        var picked = new List<LaunchItem>(count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(LaunchItem item)
        {
            if (picked.Count < count && seen.Add(item.Id)) picked.Add(item);
        }

        foreach (var u in usage.Values.OrderByDescending(u => u.Count).ThenByDescending(u => u.LastLaunched))
            if (byId.TryGetValue(u.Id, out var item)) Add(item);

        foreach (var r in recent.Where(r => r.LastRun is not null).OrderByDescending(r => r.LastRun))
            if (byId.TryGetValue(r.Name, out var item)) Add(item);

        foreach (var item in apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)) Add(item);
        return picked;
    }
}
