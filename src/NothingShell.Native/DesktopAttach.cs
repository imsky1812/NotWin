using System.Runtime.InteropServices;
using NothingShell.Core;

namespace NothingShell.Native;

/// <summary>
/// Puts widget windows on the desktop layer the way the Phase 0 spike proved (Option A, findings "Spike: desktop
/// layer"): send 0x052C to Progman, then make the widget a child of the window that hosts SHELLDLL_DefView, at the top of
/// its children — above the wallpaper and (absent) icons, below every app window, visible and clickable after Win+D.
/// Child windows are never in Alt+Tab (the spike's top-level widget was: D.4 FAIL).
/// </summary>
public static class DesktopAttach
{
    public const int WsChild = 0x40000000, WsVisible = 0x10000000, WsClipSiblings = 0x04000000;
    private const int WsPopup = unchecked((int)0x80000000), WsCaption = 0x00C00000;
    private const int WsExAppWindow = 0x00040000, WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010, SwpShowWindow = 0x0040, SwpFrameChanged = 0x0020, SwpNoZOrder = 0x0004;

    /// <summary>A window style made into a plain child window (no popup, no caption).</summary>
    public static int ChildStyle(int style) => (style | WsChild) & ~WsPopup & ~WsCaption;

    /// <summary>Extended style for a widget: a tool window that never activates; never an app window.</summary>
    public static int ChildExStyle(int ex) => (ex | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow;

    /// <summary>The desktop window hosting SHELLDLL_DefView: Progman, or (older layouts) a top-level WorkerW. 0 if none yet.</summary>
    public static nint FindHost()
    {
        var progman = FindWindowW("Progman", null);
        if (progman != 0 && FindWindowExW(progman, 0, "SHELLDLL_DefView", null) != 0) return progman;
        nint found = 0;
        EnumWindows((w, _) =>
        {
            if (FindWindowExW(w, 0, "SHELLDLL_DefView", null) != 0) { found = w; return false; }
            return true;
        }, 0);
        return found;
    }

    /// <summary>Asks Explorer to set up its desktop worker windows (the 0x052C message), as the spike did.</summary>
    public static void Prepare()
    {
        var progman = FindWindowW("Progman", null);
        if (progman != 0) SendMessageTimeoutW(progman, 0x052C, 0xD, 1, 0 /* SMTO_NORMAL */, 1000, out _);
    }

    /// <summary>Converts a screen rectangle into <paramref name="host"/>'s client coordinates.</summary>
    public static RectI ToHost(nint host, RectI screen)
    {
        var p = new Pt { X = screen.Left, Y = screen.Top };
        ScreenToClient(host, ref p);
        return new RectI(p.X, p.Y, p.X + screen.Right - screen.Left, p.Y + screen.Bottom - screen.Top);
    }

    /// <summary>Places <paramref name="child"/> (already a child of <paramref name="host"/>) at <paramref name="screen"/>, on top.</summary>
    public static void Place(nint child, nint host, RectI screen, bool raise = true)
    {
        var r = ToHost(host, screen);
        SetWindowPos(child, 0 /* HWND_TOP */, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            SwpNoActivate | SwpShowWindow | SwpFrameChanged | (raise ? 0 : SwpNoZOrder));
    }

    /// <summary>Puts <paramref name="child"/> right below <paramref name="above"/> among the host's children (edit backdrop).</summary>
    public static void PlaceBelow(nint child, nint above, nint host, RectI screen)
    {
        var r = ToHost(host, screen);
        SetWindowPos(child, above, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SwpNoActivate | SwpShowWindow);
    }

    public static nint ParentOf(nint child) => GetParent(child);

    public static bool IsAlive(nint hwnd) => hwnd != 0 && IsWindow(hwnd);

    [StructLayout(LayoutKind.Sequential)] private struct Pt { public int X, Y; }
    private delegate bool EnumProc(nint hwnd, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowExW(nint parent, nint after, string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, nint lParam);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeoutW(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint hwnd, ref Pt p);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
}
