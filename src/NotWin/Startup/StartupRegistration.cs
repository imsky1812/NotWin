using System.IO;
using Microsoft.Win32;
using NotWin.Core;

namespace NotWin.Startup;

/// <summary>
/// Per-user "start with Windows" via HKCU Run (the signed uiAccess exe launches fine from a normal-user process).
/// Only the installed copy (Program Files\NotWin) may own the entry, so dev/bin builds never hijack it.
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
            key.SetValue(ValueName, "\"" + exePath + "\"", RegistryValueKind.String);
            return;
        }

        // Disabling: the installed copy always clears it; a dev build only clears an entry that points at itself.
        if (installed || StartupPolicy.RunValuePointsAt(key.GetValue(ValueName) as string, exePath))
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        else
            Log.Info("start-with-windows: not the installed copy - Run entry left unchanged");
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string s && s.Length > 0;
    }
}
