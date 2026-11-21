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

    /// <summary>Copy only when the old folder exists and the new one holds no files yet.</summary>
    public static MigrationStep Plan(bool oldExists, bool newHasFiles) =>
        oldExists && !newHasFiles ? MigrationStep.Copy : MigrationStep.None;

    /// <summary>Copies <paramref name="oldDir"/> into <paramref name="newDir"/>; a file that fails is counted and skipped.</summary>
    public static (int Copied, int Failed) Run(string oldDir, string newDir)
    {
        int copied = 0, failed = 0;
        Directory.CreateDirectory(newDir);
        foreach (var source in Directory.EnumerateFiles(oldDir, "*", SearchOption.AllDirectories))
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
        return (copied, failed);
    }
}
