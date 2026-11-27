using System.IO;

namespace NotWin.Core.Launcher;

/// <summary>A program the user added from their PC (Plan 14): a full path to an exe or shortcut, and its display name.</summary>
public sealed record CustomApp(string Path, string Name);

/// <summary>
/// The user's own added programs (Plan 14, user 2026-11-27: a game on D: with no Start Menu entry could not be found or
/// put in a folder). Stored as full paths, no duplicates (compared ignoring case); each becomes a launchable app so it
/// shows in search and in an app folder's chooser. Pure: existence and file dialogs live in the WPF layer.
/// </summary>
public static class CustomApps
{
    /// <summary>Adds the program, named after its file. Blank or non-rooted paths are ignored; duplicates are not re-added.</summary>
    public static IReadOnlyList<CustomApp> Add(IReadOnlyList<CustomApp> list, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return list;
        var full = path.Trim();
        if (list.Any(a => string.Equals(a.Path, full, StringComparison.OrdinalIgnoreCase))) return list;
        return [.. list, new CustomApp(full, NameFor(full))];
    }

    public static IReadOnlyList<CustomApp> Remove(IReadOnlyList<CustomApp> list, string path) =>
        list.Where(a => !string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The name shown for a program: its file name without the extension.</summary>
    public static string NameFor(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    /// <summary>A launchable app whose id and target are the path itself (ShellExecute runs the exe or shortcut).</summary>
    public static LaunchItem ToItem(CustomApp app) => new(app.Path, app.Name, LaunchKind.App, app.Path, []);
}
