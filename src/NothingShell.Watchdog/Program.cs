using System.Diagnostics;
using NothingShell.Core;
using NothingShell.Native;

// Tiny out-of-process safety net: if the shell dies, give the Windows taskbar back.
// No window, no logging: result is conveyed by exit code (0 ok, 2 bad arguments).
return Run(args);

static int Run(string[] args)
{
    int? parentPid = null;
    string? stateDir = null;
    var restoreNow = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--restore-now": restoreNow = true; break;
            case "--parent" when i + 1 < args.Length && int.TryParse(args[++i], out var pid) && pid > 0:
                parentPid = pid; break;
            case "--state" when i + 1 < args.Length: stateDir = args[++i]; break;
            default: return 2;
        }
    }

    if (string.IsNullOrWhiteSpace(stateDir) || restoreNow == parentPid.HasValue) return 2;

    if (parentPid is { } p)
    {
        try
        {
            using var parent = Process.GetProcessById(p);
            parent.WaitForExit();
        }
        catch (ArgumentException)
        {
            // Parent already gone: fall through and restore immediately.
        }
    }

    var store = new TaskbarRestoreStore(stateDir);
    if (store.TryLoad(out var state))
    {
        TaskbarNative.Restore(state);
        try { store.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    return 0;
}
