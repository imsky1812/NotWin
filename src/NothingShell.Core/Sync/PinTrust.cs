namespace NothingShell.Core.Sync;

/// <summary>
/// Which launch paths a synced pin may bring from another PC (security review 2026-11-21, #4): an app id
/// (<c>shell:AppsFolder\…</c>, resolved by Windows from installed apps) or a local path under a folder where installed
/// apps and their shortcuts live (Program Files, Windows, the user's per-user Programs and Start Menu). Never a network
/// path, a relative path, a path with "..", or anything in a synced or shared folder.
/// </summary>
public static class PinTrust
{
    private const string AppsFolder = @"shell:AppsFolder\";

    public static bool IsTrusted(string? path, IReadOnlyList<string> trustedRoots)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.StartsWith(AppsFolder, StringComparison.OrdinalIgnoreCase))
        {
            var id = path[AppsFolder.Length..];
            return id.Length > 0 && !id.Contains('\\') && !id.Contains('/') && !id.Contains("..", StringComparison.Ordinal);
        }
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return false;
        if (path.Split('\\', '/').Any(segment => segment == "..")) return false;
        if (!Path.IsPathFullyQualified(path)) return false;
        foreach (var root in trustedRoots)
        {
            var r = root.TrimEnd('\\', '/') + "\\";
            if (path.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
