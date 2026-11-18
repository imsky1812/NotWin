using System.Runtime.InteropServices;
using NothingShell.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NothingShell.Native;

/// <summary>
/// Controls the real Windows taskbar. Only windows owned by explorer.exe are touched: third-party
/// taskbars (e.g. Fenn) register decoy Shell_TrayWnd windows in their own process, which are ignored.
/// </summary>
public static class TaskbarNative
{
    private const string PrimaryClass = "Shell_TrayWnd";
    private const string SecondaryClass = "Shell_SecondaryTrayWnd";
    private const uint AbmGetState = 4;
    private const uint AbmSetState = 10;
    private const uint AbsAutoHide = 1;

    public static bool GetAutoHide()
    {
        var data = NewAppBarData(FindPrimary());
        var state = (uint)PInvoke.SHAppBarMessage(AbmGetState, ref data);
        return (state & AbsAutoHide) != 0;
    }

    public static void SetAutoHide(bool on)
    {
        var data = NewAppBarData(FindPrimary());
        var state = (uint)PInvoke.SHAppBarMessage(AbmGetState, ref data);
        data.lParam = (nint)(on ? (state | AbsAutoHide) : (state & ~AbsAutoHide));
        PInvoke.SHAppBarMessage(AbmSetState, ref data);
    }

    public static void HideAll() => ShowAllWindows(SHOW_WINDOW_CMD.SW_HIDE);

    /// <summary>Shows every explorer-owned tray window. Returns false when no explorer tray exists (e.g. Explorer restarting).</summary>
    public static bool ShowAll() => ShowAllWindows(SHOW_WINDOW_CMD.SW_SHOW);

    /// <summary>True when an explorer-owned primary tray window exists.</summary>
    public static bool HasTray() => FindPrimary() != HWND.Null;

    /// <summary>True when the explorer-owned primary taskbar is visible (false if not found).</summary>
    public static bool IsPrimaryVisible()
    {
        var h = FindPrimary();
        return h != HWND.Null && PInvoke.IsWindowVisible(h);
    }

    /// <summary>Shows the taskbar, then applies the saved auto-hide. Returns false (doing nothing more) if no explorer tray exists.</summary>
    public static bool Restore(TaskbarRestoreState s)
    {
        if (!ShowAll()) return false;
        SetAutoHide(s.WasAutoHide);
        return true;
    }

    /// <summary>True when <paramref name="hwnd"/> is an explorer-owned Shell_TrayWnd (not a third-party decoy such as Fenn's).</summary>
    public static bool IsExplorerTrayWindow(nint hwnd)
    {
        if (hwnd == 0) return false;
        Span<char> cls = stackalloc char[64];
        var n = PInvoke.GetClassName(new HWND(hwnd), cls);
        return n > 0 && cls[..n].SequenceEqual(PrimaryClass) && IsExplorerOwned(new HWND(hwnd));
    }

    private static APPBARDATA NewAppBarData(HWND hwnd) => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = hwnd,
    };

    private static HWND FindPrimary()
    {
        foreach (var h in FindExplorerWindows(PrimaryClass)) return h;
        return HWND.Null;
    }

    private static bool ShowAllWindows(SHOW_WINDOW_CMD cmd)
    {
        var primaries = FindExplorerWindows(PrimaryClass);
        foreach (var h in primaries) PInvoke.ShowWindow(h, cmd);
        foreach (var h in FindExplorerWindows(SecondaryClass)) PInvoke.ShowWindow(h, cmd);
        return primaries.Count > 0;
    }

    private static List<HWND> FindExplorerWindows(string className)
    {
        var result = new List<HWND>();
        HWND prev = HWND.Null;
        while (true)
        {
            prev = PInvoke.FindWindowEx(HWND.Null, prev, className, null);
            if (prev == HWND.Null) break;
            if (IsExplorerOwned(prev)) result.Add(prev);
        }
        return result;
    }

    private static unsafe bool IsExplorerOwned(HWND hwnd)
    {
        uint pid = 0;
        if (PInvoke.GetWindowThreadProcessId(hwnd, &pid) == 0 || pid == 0) return false;
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsInvalid) return false;
        Span<char> buffer = stackalloc char[1024];
        uint len = (uint)buffer.Length;
        if (!PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref len))
            return false;
        var path = new string(buffer[..(int)len]);
        return string.Equals(Path.GetFileName(path), "explorer.exe", StringComparison.OrdinalIgnoreCase);
    }
}
