using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NotWin.Shell;

/// <summary>Starts NotWin.Watchdog.exe (next to the app) to restore the taskbar if the app dies.</summary>
public static class WatchdogLauncher
{
    private const string ExeName = "NotWin.Watchdog.exe";

    /// <summary>Returns the watchdog process, or null (logged) when the exe is missing or could not be launched.</summary>
    public static Process? Start(int parentPid, string stateDir)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (!File.Exists(exe))
        {
            Log.Error($"{ExeName} not found at {exe}");
            return null;
        }

        if (!GetProcessTimes(GetCurrentProcess(), out var created, out _, out _, out _))
        {
            Log.Error($"GetProcessTimes failed ({Marshal.GetLastWin32Error()})");
            return null;
        }
        // Exact UTC creation FILETIME of this process (no local-time/DST round trip).
        var startFileTime = ((long)created.dwHighDateTime << 32) | (uint)created.dwLowDateTime;
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
        try { return Process.Start(psi); }
        catch (Exception e)
        {
            Log.Error("failed to launch watchdog", e);
            return null;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out System.Runtime.InteropServices.ComTypes.FILETIME creation,
        out System.Runtime.InteropServices.ComTypes.FILETIME exit, out System.Runtime.InteropServices.ComTypes.FILETIME kernel,
        out System.Runtime.InteropServices.ComTypes.FILETIME user);
}
