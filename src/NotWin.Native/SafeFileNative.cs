using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NotWin.Native;

/// <summary>
/// Deleting a file from a folder a normal-integrity process can write, while running elevated (security review
/// 2026-11-21, #2: the install / uninstall scripts run the watchdog's "--restore-now" as admin on
/// %AppData%\…\taskbar-restore.json). The file is opened without following a link at its own name, then deleted only
/// when Windows' final path for that handle is exactly <c>folder\name</c>: a junction for the folder, or a link at the
/// file, points somewhere else and is refused.
/// </summary>
public static class SafeFileNative
{
    private const uint Delete = 0x00010000, ReadAttributes = 0x80, ShareAll = 0x7, OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000;
    private const uint AttrDirectory = 0x10, AttrReparsePoint = 0x400;
    private const int FileDispositionInfo = 4;

    /// <summary>Deletes <paramref name="file"/> if it really is a plain file directly in <paramref name="folder"/>; false otherwise.</summary>
    public static bool DeleteInside(string file, string folder)
    {
        try
        {
            var expected = Path.Combine(Path.GetFullPath(folder), Path.GetFileName(file));
            if (!string.Equals(Path.GetFullPath(file), expected, StringComparison.OrdinalIgnoreCase)) return false;
            using var h = CreateFileW(expected, Delete | ReadAttributes, ShareAll, 0, OpenExisting, OpenReparsePoint | BackupSemantics, 0);
            if (h.IsInvalid) return false;
            if (!GetFileInformationByHandle(h, out var info)) return false;
            if ((info.Attributes & (AttrDirectory | AttrReparsePoint)) != 0) return false;
            var buffer = new char[1024];
            var n = GetFinalPathNameByHandleW(h, buffer, (uint)buffer.Length, 0);
            if (n == 0 || n >= buffer.Length) return false;
            var final = new string(buffer, 0, (int)n);
            if (final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return false;
            if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) final = final[4..];
            if (!string.Equals(final, expected, StringComparison.OrdinalIgnoreCase)) return false;
            var dispose = 1; // FILE_DISPOSITION_INFO.DeleteFile = TRUE
            return SetFileInformationByHandle(h, FileDispositionInfo, ref dispose, sizeof(int));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint Attributes;
        public long CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle h, out ByHandleFileInformation info);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, [Out] char[] path, uint size, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle h, int infoClass, ref int info, int size);
}
