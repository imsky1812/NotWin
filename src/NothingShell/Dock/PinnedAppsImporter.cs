using System.IO;
using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Dock;

/// <summary>
/// First-run import of the Windows taskbar pins (<c>...\Quick Launch\User Pinned\TaskBar\*.lnk</c>).
/// AppId follows the WindowTracker convention: the shortcut's AppUserModel.ID, else the lower-case target exe path.
/// UI (STA) thread only: it uses COM.
/// </summary>
public static class PinnedAppsImporter
{
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    public static IReadOnlyList<PinnedApp> Import(string userPinnedDir)
    {
        var result = new List<PinnedApp>();
        if (!Directory.Exists(userPinnedDir))
        {
            Log.Info($"pin import: folder not found ({userPinnedDir})");
            return result;
        }

        // Top directory only (not the Tombstones subfolder), .lnk files only, taskbar order approximated by name.
        string[] files;
        try { files = Directory.GetFiles(userPinnedDir, "*.lnk", SearchOption.TopDirectoryOnly); }
        catch (Exception e) { Log.Error("pin import: cannot list folder", e); return result; }
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lnk in files)
        {
            var name = Path.GetFileNameWithoutExtension(lnk);
            var info = ShellLinkNative.Read(lnk);
            if (info is null) { Log.Info($"pin import: skipped '{name}' (unreadable shortcut)"); continue; }
            var (target, aumid) = info.Value;

            var targetExists = !string.IsNullOrEmpty(target) && File.Exists(target);
            var pin = PinRules.FromShortcut(name, lnk, target, targetExists, aumid);
            if (pin is null) { Log.Info($"pin import: skipped '{name}' (target missing and no AppUserModel.ID)"); continue; }
            if (!seen.Add(pin.AppId)) continue;

            result.Add(pin);
            Log.Info($"pin import: '{name}' appId={pin.AppId} launch={pin.LaunchPath}");
        }
        return result;
    }

    /// <summary>
    /// On the first run (<c>PinsImported == false</c>) imports the taskbar pins and saves them. Failures are logged;
    /// the flag is only set when the import itself ran, so a crash mid-way retries next time.
    /// </summary>
    public static ShellSettings ApplyFirstRun(ShellSettings settings, SettingsStore store)
    {
        if (settings.PinsImported) return settings;
        try
        {
            var imported = Import(DefaultDirectory);
            var merged = settings.PinnedApps.ToList();
            foreach (var p in imported)
                if (!merged.Any(m => string.Equals(m.AppId, p.AppId, StringComparison.OrdinalIgnoreCase))) merged.Add(p);
            settings = settings with { PinnedApps = merged, PinsImported = true };
            store.Save(settings);
        }
        catch (Exception e) { Log.Error("pin import failed", e); }
        return settings;
    }
}
