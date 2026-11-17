using System.Diagnostics;
using System.IO;

namespace NothingShell.Shell;

/// <summary>Starts NothingShell.Watchdog.exe (next to the app) to restore the taskbar if the app dies.</summary>
public static class WatchdogLauncher
{
    private const string ExeName = "NothingShell.Watchdog.exe";

    /// <summary>Returns the watchdog process, or null (with a logged warning) in Debug when the exe is absent.</summary>
    public static Process? Start(int parentPid, string stateDir)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (!File.Exists(exe))
        {
#if DEBUG
            Log.Info($"WARNING: {ExeName} not found at {exe}; running WITHOUT the crash watchdog (Debug only).");
            return null;
#else
            throw new FileNotFoundException("Watchdog executable missing.", exe);
#endif
        }

        var startFileTime = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToFileTimeUtc();
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add("--parent");
        psi.ArgumentList.Add(parentPid.ToString());
        psi.ArgumentList.Add("--parent-start");
        psi.ArgumentList.Add(startFileTime.ToString());
        psi.ArgumentList.Add("--state");
        psi.ArgumentList.Add(stateDir);
        return Process.Start(psi);
    }
}
