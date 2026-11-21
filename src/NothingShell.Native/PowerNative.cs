using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Shutdown;
using Windows.Win32.System.Threading;

namespace NothingShell.Native;

/// <summary>The launcher's power menu: sleep, restart, shut down. Restart/shut down act immediately (user decision).</summary>
public static unsafe class PowerNative
{
    // SetSuspendState is documented to need SeShutdownPrivilege, like restart/shut down.
    public static bool Sleep() => EnableShutdownPrivilege() && PInvoke.SetSuspendState(false, false, false);

    public static bool Restart() => Exit(EXIT_WINDOWS_FLAGS.EWX_REBOOT);

    public static bool ShutDown() => Exit(EXIT_WINDOWS_FLAGS.EWX_SHUTDOWN | EXIT_WINDOWS_FLAGS.EWX_POWEROFF);

    private static bool Exit(EXIT_WINDOWS_FLAGS how)
    {
        if (!EnableShutdownPrivilege()) return false;
        return PInvoke.ExitWindowsEx(how, SHUTDOWN_REASON.SHTDN_REASON_FLAG_PLANNED);
    }

    private static bool EnableShutdownPrivilege()
    {
        HANDLE token = default;
        try
        {
            if (!PInvoke.OpenProcessToken(PInvoke.GetCurrentProcess(),
                    TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY, &token)) return false;
            LUID luid;
            fixed (char* name = "SeShutdownPrivilege")
                if (!PInvoke.LookupPrivilegeValue(null, name, &luid)) return false;
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1 };
            tp.Privileges[0] = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED };
            return PInvoke.AdjustTokenPrivileges(token, false, &tp, 0, null, null);
        }
        finally { if (!token.IsNull) PInvoke.CloseHandle(token); }
    }
}
