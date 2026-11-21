namespace NotWin.Core;

/// <summary>Finds other shells that fight over the taskbar/notch. Pure: callers pass process names.</summary>
public static class ConflictDetector
{
    public static string? FindConflict(IEnumerable<string> processNames)
    {
        foreach (var name in processNames)
            if (string.Equals(name, "fenn", StringComparison.OrdinalIgnoreCase)) return "Fenn";
        return null;
    }
}
