using System.Runtime.InteropServices;

namespace NothingShell.Interop;

/// <summary>Minimal token query; superseded by CsWin32 once it is introduced.</summary>
internal static partial class TokenInfo
{
    private const uint TokenQuery = 0x0008;
    private const int TokenUIAccess = 26;

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int infoClass, out uint info, uint length, out uint returned);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    public static bool GetUiAccess()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token)) return false;
        try
        {
            return GetTokenInformation(token, TokenUIAccess, out var value, sizeof(uint), out _) && value != 0;
        }
        finally { CloseHandle(token); }
    }
}
