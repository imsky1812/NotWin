namespace NothingShell.Core;

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

    /// <summary>True when an existing Run value (quoted or not) points at exactly this exe.</summary>
    public static bool RunValuePointsAt(string? runValue, string exePath)
    {
        if (string.IsNullOrWhiteSpace(runValue) || string.IsNullOrWhiteSpace(exePath)) return false;
        return string.Equals(runValue.Trim().Trim('"'), exePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string p) => p.TrimEnd('\\', '/');
}
