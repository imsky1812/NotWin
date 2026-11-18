using NothingShell.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Shell.PropertiesSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NothingShell.Native;

/// <summary>One top-level window the dock considers an "app window". Raw facts; grouping happens in Core.</summary>
public readonly record struct RawWindow(nint Handle, uint Pid, string Class, string Title);

/// <summary>Win32 pieces behind WindowTracker: enumeration, per-window identity and activation.</summary>
public static unsafe class WindowListNative
{
    private const int WsExToolWindow = 0x80;
    private const int WsExAppWindow = 0x40000;

    /// <summary>
    /// Visible, unowned, non-tool-window, non-cloaked top-level windows, in z-order (top first).
    /// Windows of <paramref name="ownPid"/> and the desktop (Progman/WorkerW) are left out.
    /// </summary>
    public static List<RawWindow> EnumerateAppWindows(uint ownPid)
    {
        s_result = new List<RawWindow>();
        s_ownPid = ownPid;
        try { PInvoke.EnumWindows(&EnumProc, 0); }
        finally { }
        var r = s_result;
        s_result = null;
        return r;
    }

    // EnumWindows takes a function pointer, which cannot capture: state lives in thread-static slots.
    [ThreadStatic] private static List<RawWindow>? s_result;
    [ThreadStatic] private static uint s_ownPid;

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static BOOL EnumProc(HWND hwnd, LPARAM lParam)
    {
        try
        {
            if (!PInvoke.IsWindowVisible(hwnd)) return true;
            if (PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER) != HWND.Null) return true;
            var ex = (nint)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
            if ((ex & WsExToolWindow) != 0 && (ex & WsExAppWindow) == 0) return true;
            int cloaked = 0;
            if (PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int)).Succeeded && cloaked != 0)
                return true;

            uint pid;
            PInvoke.GetWindowThreadProcessId(hwnd, &pid);
            if (pid == s_ownPid) return true;

            var cls = GetClass(hwnd);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            s_result?.Add(new RawWindow(hwnd, pid, cls, GetTitle(hwnd)));
        }
        catch { /* skip a window we cannot read */ }
        return true;
    }

    public static string GetTitle(nint hwnd)
    {
        var h = (HWND)hwnd;
        var len = PInvoke.GetWindowTextLength(h);
        if (len <= 0) return "";
        var buf = new char[len + 1];
        fixed (char* p = buf)
        {
            var n = PInvoke.GetWindowText(h, p, buf.Length);
            return new string(p, 0, n);
        }
    }

    public static string GetClass(nint hwnd)
    {
        Span<char> buf = stackalloc char[256];
        fixed (char* p = buf)
        {
            var n = PInvoke.GetClassName((HWND)hwnd, p, buf.Length);
            return new string(p, 0, n);
        }
    }

    public static string? GetProcessPath(uint pid)
    {
        var h = PInvoke.OpenProcess(Windows.Win32.System.Threading.PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h.IsNull) return null;
        try
        {
            var buf = new char[1024];
            uint len = (uint)buf.Length;
            fixed (char* p = buf)
            {
                if (!PInvoke.QueryFullProcessImageName(h, Windows.Win32.System.Threading.PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, p, &len))
                    return null;
                return new string(p, 0, (int)len);
            }
        }
        finally { PInvoke.CloseHandle(h); }
    }

    /// <summary>First child window of the given class (e.g. the CoreWindow hosted by an ApplicationFrameHost frame).</summary>
    public static nint FindChild(nint parent, string className) =>
        PInvoke.FindWindowEx((HWND)parent, HWND.Null, className, null);

    public static uint GetPid(nint hwnd)
    {
        uint pid;
        PInvoke.GetWindowThreadProcessId((HWND)hwnd, &pid);
        return pid;
    }

    /// <summary>The window's System.AppUserModel.ID, or null when it has none.</summary>
    public static string? GetAppUserModelId(nint hwnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        void* raw;
        if (PInvoke.SHGetPropertyStoreForWindow((HWND)hwnd, &iid, &raw).Failed || raw is null) return null;
        var store = (IPropertyStore*)raw;
        try
        {
            var key = PInvoke.PKEY_AppUserModel_ID;
            Windows.Win32.System.Com.StructuredStorage.PROPVARIANT pv;
            try { store->GetValue(&key, &pv); } catch { return null; }
            try
            {
                PWSTR s;
                if (PInvoke.PropVariantToStringAlloc(&pv, &s).Failed) return null;
                try { var str = s.ToString(); return string.IsNullOrWhiteSpace(str) ? null : str; }
                finally { PInvoke.CoTaskMemFree(s); }
            }
            finally { PInvoke.PropVariantClear(&pv); }
        }
        finally { store->Release(); }
    }

    /// <summary>The foreground window's root owner, so a dialog counts as its parent window being focused.</summary>
    public static nint GetForegroundRoot()
    {
        var fg = PInvoke.GetForegroundWindow();
        if (fg == HWND.Null) return 0;
        var root = PInvoke.GetAncestor(fg, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);
        return root == HWND.Null ? fg : root;
    }

    /// <summary>Restores the window if minimized and brings it to the foreground. False if Windows refused.</summary>
    public static bool Activate(nint hwnd)
    {
        var h = (HWND)hwnd;
        if (PInvoke.IsIconic(h)) PInvoke.ShowWindow(h, SHOW_WINDOW_CMD.SW_RESTORE);
        return PInvoke.SetForegroundWindow(h);
    }

    public static void Minimize(nint hwnd) => PInvoke.ShowWindow((HWND)hwnd, SHOW_WINDOW_CMD.SW_MINIMIZE);

    public static void RequestClose(nint hwnd) => PInvoke.PostMessage((HWND)hwnd, 0x0010 /* WM_CLOSE */, 0, 0);
}
