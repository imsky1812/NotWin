namespace NothingShell.Core.Widgets;

/// <summary>
/// A photo frame's photos (Plan 5b): in the order added, each added batch sorted by date taken (the Windows picker
/// gives no click order), at most <see cref="Max"/>.
/// </summary>
public static class PhotoList
{
    public const int Max = 24;

    /// <summary>The list with the batch appended (sorted by date taken) up to <see cref="Max"/>; Skipped = what did not fit.</summary>
    public static (IReadOnlyList<string> List, int Skipped) Append(IReadOnlyList<string> existing, IReadOnlyList<(string File, DateTime Taken)> batch)
    {
        var list = existing.ToList();
        var skipped = 0;
        foreach (var (file, _) in batch.OrderBy(b => b.Taken))
        {
            if (list.Count >= Max) { skipped++; continue; }
            list.Add(file);
        }
        return (list, skipped);
    }

    /// <summary>The slideshow's next index (wraps; 0 for an empty or single-photo list).</summary>
    public static int Next(int index, int count) => count <= 1 ? 0 : (index + 1) % count;
}

/// <summary>An app folder's apps (Plan 5b): launcher app ids, no duplicates; uninstalled apps are skipped when shown.</summary>
public static class FolderApps
{
    public static IReadOnlyList<string> Add(IReadOnlyList<string> apps, string id) => apps.Contains(id) ? apps : [.. apps, id];

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> apps, string id) => apps.Where(a => a != id).ToList();

    public static IReadOnlyList<T> Resolve<T>(IReadOnlyList<string> ids, Func<string, T?> find) where T : class =>
        ids.Select(find).OfType<T>().ToList();
}
