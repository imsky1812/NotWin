namespace NotWin.Core;

/// <summary>Decides when the HKCU Run entry may be touched, so dev/bin builds never hijack the installed copy's entry.</summary>
public static class StartupPolicy
{
    /// <summary>True only when the exe lives directly in the install folder (case-insensitive).</summary>
    public static bool ShouldWriteRunValue(string exePath, string installDir)
    {
        if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(installDir)) return false;
        var dir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(dir)) return false;
        return string.Equals(Normalize(dir), Normalize(installDir), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The start-with-Windows command (user 2026-11-28): the shell (explorer.exe) launches NotWin, so it is granted
    /// uiAccess at logon. A uiAccess exe run straight from the Run key is often launched without uiAccess, or UAC
    /// refuses it, so the dock can't take over the shell after a cold boot.
    /// </summary>
    public static string RunCommand(string exePath) => $"explorer.exe \"{exePath}\"";

    private const string ExplorerPrefix = "explorer.exe ";

    /// <summary>True when an existing Run value points at exactly this exe — the bare "exe" or the explorer-wrapped form.</summary>
    public static bool RunValuePointsAt(string? runValue, string exePath)
    {
        if (string.IsNullOrWhiteSpace(runValue) || string.IsNullOrWhiteSpace(exePath)) return false;
        var v = runValue.Trim();
        if (v.StartsWith(ExplorerPrefix, StringComparison.OrdinalIgnoreCase)) v = v[ExplorerPrefix.Length..].Trim();
        return string.Equals(v.Trim('"'), exePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string p) => p.TrimEnd('\\', '/');
}
