namespace NothingShell.Core.Launcher;

/// <summary>One item of <c>shell:AppsFolder</c>: its parsing name (AUMID or known-folder path) and display name.</summary>
public readonly record struct AppsFolderEntry(string ParsingName, string DisplayName);

/// <summary>Which AppsFolder entries are worth showing as apps (drops uninstallers, docs, links).</summary>
public static class AppIndexRules
{
    private static readonly string[] DocExtensions = [".url", ".chm", ".txt", ".pdf", ".html", ".htm"];
    private static readonly string[] DocNames = ["readme", "help", "release notes"];

    public static bool IsLaunchable(AppsFolderEntry e)
    {
        var name = e.DisplayName?.Trim() ?? "";
        if (name.Length == 0) return false;
        if (name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase)) return false;
        if (DocNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) return false;
        var parsing = e.ParsingName ?? "";
        return !DocExtensions.Any(x => parsing.EndsWith(x, StringComparison.OrdinalIgnoreCase));
    }
}
