namespace NotWin.Startup;

/// <summary>
/// The names the app had before it was renamed NotWin (Plan 12, 2026-11-21): its data folder under %AppData% (carried
/// over once at start-up) and its start-with-Windows value (removed if a previous install left it behind).
/// </summary>
public static class LegacyNames
{
    public const string DataFolder = "NothingShell";
    public const string RunValue = "NothingShell";
}
