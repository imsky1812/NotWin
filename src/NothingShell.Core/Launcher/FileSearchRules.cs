namespace NothingShell.Core.Launcher;

public static class FileSearchRules
{
    /// <summary>Recent items first, then index results; one row per path (case-insensitive); at most <paramref name="max"/>.</summary>
    public static IReadOnlyList<LaunchItem> Merge(IReadOnlyList<LaunchItem> recent, IReadOnlyList<LaunchItem> indexed, int max)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return recent.Concat(indexed).Where(i => seen.Add(i.Id)).Take(max).ToList();
    }
}
