using System.Globalization;
using System.IO;

namespace NothingShell.Sync;

/// <summary>
/// The Settings sync line (Plan 10): what really happened. Nothing Shell only saves into the folder; OneDrive (or the
/// folder's own sync app) uploads it from there, so "synced" would claim more than the shell knows.
/// </summary>
public static class SyncStatus
{
    public static string Text(string? folder, bool reached, DateTime? at, IEnumerable<string> oneDriveRoots)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "OFF";
        if (!reached || at is not { } when) return "CAN'T REACH THE FOLDER";
        var uploader = InOneDrive(folder, oneDriveRoots) ? "ONEDRIVE" : "ITS SYNC APP";
        return string.Create(CultureInfo.InvariantCulture, $"SAVED TO THE FOLDER {when:HH:mm} · {uploader} UPLOADS IT");
    }

    /// <summary>The OneDrive folders Windows knows (personal and work), from its environment variables.</summary>
    public static IReadOnlyList<string> OneDriveRoots() =>
        [.. new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
            .Select(Environment.GetEnvironmentVariable).OfType<string>().Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static bool InOneDrive(string folder, IEnumerable<string> roots)
    {
        var f = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return roots.Any(r => f.StartsWith(Path.TrimEndingDirectorySeparator(r) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }
}
