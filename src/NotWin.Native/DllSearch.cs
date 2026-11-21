using System.Runtime.InteropServices;

namespace NotWin.Native;

/// <summary>
/// Security review 2026-11-21 (hardening): DLLs load only from the app's own folder and Windows' System32, never from
/// folders on the user's PATH or the current directory, so a DLL planted there is never loaded into the raised
/// (uiAccess) process. Called first thing by every exe.
/// </summary>
public static class DllSearch
{
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;

    public static void Restrict()
    {
        try { SetDefaultDllDirectories(LoadLibrarySearchDefaultDirs); }
        catch (EntryPointNotFoundException) { } // very old Windows: keep the default order
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint flags);
}
