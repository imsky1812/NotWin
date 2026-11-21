using System.Diagnostics;
using NotWin.Core;
using NotWin.Native;

// Tiny out-of-process safety net: if the shell dies, give the Windows taskbar back.
// No window, no logging: result is conveyed by exit code.
//   0 = done (restored, or nothing to restore)   2 = bad arguments   3 = no explorer taskbar appeared in time
//
// Usage:
//   --parent <pid> [--parent-start <utc FILETIME>] --state <dir>   wait for the parent to exit, then restore
//   --restore-now --state <dir>                                    restore immediately
// --parent-start guards against PID reuse: if the process with that pid started at a different time
// (+/- 1 s) it is not our parent, so the parent is treated as already gone.
DllSearch.Restrict(); // only the app folder and System32 (security review 2026-11-21)
return Run(args);

static int Run(string[] args)
{
    int? parentPid = null;
    long? parentStart = null;
    string? stateDir = null;
    var restoreNow = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--restore-now": restoreNow = true; break;
            case "--parent" when i + 1 < args.Length && int.TryParse(args[++i], out var pid) && pid > 0:
                parentPid = pid; break;
            case "--parent-start" when i + 1 < args.Length && long.TryParse(args[++i], out var ft) && ft > 0:
                parentStart = ft; break;
            case "--state" when i + 1 < args.Length: stateDir = args[++i]; break;
            default: return 2;
        }
    }

    if (string.IsNullOrWhiteSpace(stateDir) || restoreNow == parentPid.HasValue) return 2;
    if (parentStart.HasValue && !parentPid.HasValue) return 2;

    if (parentPid is { } p)
    {
        try
        {
            using var parent = Process.GetProcessById(p);
            if (parentStart is { } expected && !StartMatches(parent, expected)) { /* PID reused: parent is gone */ }
            else parent.WaitForExit();
        }
        catch (Exception)
        {
            // Parent gone or not inspectable: restore anyway.
        }
    }

    try
    {
        return RestoreTaskbar(new TaskbarRestoreStore(stateDir));
    }
    catch (Exception)
    {
        return 3;
    }
}

static bool StartMatches(Process parent, long expectedFileTimeUtc)
{
    var actual = parent.StartTime.ToUniversalTime().ToFileTimeUtc();
    return Math.Abs(actual - expectedFileTimeUtc) <= TimeSpan.TicksPerSecond;
}

static int RestoreTaskbar(TaskbarRestoreStore store)
{
    // Explorer may be mid-restart: wait up to ~10 s for its tray window before acting.
    var deadline = Environment.TickCount64 + 10_000;
    while (!TaskbarNative.HasTray())
    {
        if (Environment.TickCount64 >= deadline) return 3; // keep the state file for a later retry
        Thread.Sleep(250);
    }

    // Unknown original auto-hide (no/corrupt state): only show the taskbar, leave auto-hide alone.
    var acted = store.TryLoad(out var state) ? TaskbarNative.Restore(state) : TaskbarNative.ShowAll();
    if (!acted) return 3;

    // Never a plain File.Delete: this may run elevated on a folder a normal process can write (security review #2).
    SafeFileNative.DeleteInside(store.FilePath, store.Directory);
    return 0;
}
