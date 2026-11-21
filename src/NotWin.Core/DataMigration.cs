namespace NotWin.Core;

public enum MigrationStep { None, Copy }

/// <summary>
/// Plan 12 (Nothing Shell → NotWin): the first start of NotWin copies the old data folder (%AppData%\NothingShell) into
/// the new one, once, never overwriting. The old session's taskbar state and old logs are left behind; a marker is
/// written last; the old folder stays as a backup. File names are never logged — only counts.
/// </summary>
public static class DataMigration
{
    public const string Marker = "migrated-from-nothingshell.txt";

    /// <summary>Top-level files never carried over (compared case-insensitively).</summary>
    public static readonly IReadOnlySet<string> Skip =
        new HashSet<string>(["taskbar-restore.json", "log.txt", "log.1.txt", "draghelper.log"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Written first and removed last: while it exists, a copy was cut short and the next start finishes it.</summary>
    public const string InProgressMarker = "migration-in-progress.txt";

    // Unreadable folders are skipped instead of ending the walk (review 2026-11-21).
    private static readonly EnumerationOptions Walk = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };

    /// <summary>
    /// The folder holds data (a file other than the skipped logs / state), so nothing is carried over. A copy that was
    /// cut short (<see cref="InProgressMarker"/> still there) counts as no data, so it is finished.
    /// </summary>
    public static bool HasData(string dir) =>
        Directory.Exists(dir) && !File.Exists(Path.Combine(dir, InProgressMarker)) &&
        Directory.EnumerateFiles(dir, "*", Walk)
            .Select(f => Path.GetRelativePath(dir, f))
            .Any(rel => rel.Contains(Path.DirectorySeparatorChar) || !Skip.Contains(rel));

    /// <summary>Copy only when the old folder exists and the new one holds no files yet.</summary>
    public static MigrationStep Plan(bool oldExists, bool newHasFiles) =>
        oldExists && !newHasFiles ? MigrationStep.Copy : MigrationStep.None;

    /// <summary>Copies <paramref name="oldDir"/> into <paramref name="newDir"/>; a file that fails is counted and skipped.</summary>
    public static (int Copied, int Failed) Run(string oldDir, string newDir)
    {
        int copied = 0, failed = 0;
        Directory.CreateDirectory(newDir);
        var inProgress = Path.Combine(newDir, InProgressMarker);
        File.WriteAllText(inProgress, DateTime.Now.ToString("O")); // repeating the copy is safe: it never overwrites
        foreach (var source in Directory.EnumerateFiles(oldDir, "*", Walk))
        {
            var relative = Path.GetRelativePath(oldDir, source);
            if (!relative.Contains(Path.DirectorySeparatorChar) && Skip.Contains(relative)) continue;
            var target = Path.Combine(newDir, relative);
            try
            {
                if (File.Exists(target)) continue; // never overwrite
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: false);
                copied++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
        }
        File.WriteAllText(Path.Combine(newDir, Marker), $"copied {copied}, failed {failed}, {DateTime.Now:O}");
        File.Delete(inProgress);
        return (copied, failed);
    }
}
