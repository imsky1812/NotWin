using System.IO;
using Microsoft.Win32;
using NotWin.Core;

namespace NotWin.Startup;

/// <summary>
/// Per-user "start with Windows" via HKCU Run. The value launches NotWin through the shell (explorer.exe) so it is
/// granted uiAccess at logon (a uiAccess exe run straight from Run is often started without it). Only the installed
/// copy (Program Files\NotWin) may own the entry, so dev/bin builds never hijack it.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NotWin";

    private static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NotWin");

    public static void SetEnabled(bool enabled, string exePath)
    {
        var installed = StartupPolicy.ShouldWriteRunValue(exePath, InstallDir);
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            if (!installed) { Log.Info("start-with-windows: not the installed copy - Run entry left unchanged"); return; }
            // Launch through the shell (explorer.exe) so NotWin is granted uiAccess at logon (user 2026-11-28: after a
            // cold boot a uiAccess exe run straight from Run came up without it, leaving the Windows taskbar in charge).
            key.SetValue(ValueName, StartupPolicy.RunCommand(exePath), RegistryValueKind.String);
            return;
        }

        // Disabling: the installed copy always clears it; a dev build only clears an entry that points at itself.
        if (installed || StartupPolicy.RunValuePointsAt(key.GetValue(ValueName) as string, exePath))
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        else
            Log.Info("start-with-windows: not the installed copy - Run entry left unchanged");
    }

    /// <summary>
    /// Removes the start-with-Windows value an install under the old name left behind (Plan 12), so the old program can
    /// never start alongside NotWin. True when one was removed.
    /// </summary>
    public static bool RemoveLegacyRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(LegacyNames.RunValue) is null) return false;
        key.DeleteValue(LegacyNames.RunValue, throwOnMissingValue: false);
        return true;
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string s && s.Length > 0;
    }
}
