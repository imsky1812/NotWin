using NothingShell.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NothingShell.Native;

/// <summary>Win32 pieces behind the dock window: styles, placement and monitor/DPI queries.</summary>
public static unsafe class DockWindowNative
{
    public const int WsExTopmost = 0x00000008;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExAppWindow = 0x00040000;
    public const int WsExLayered = 0x00080000;
    public const int WsExNoActivate = 0x08000000;

    public const int WmDisplayChange = 0x007E;
    public const int WmDpiChanged = 0x02E0;

    private static readonly HWND HwndTopmost = (HWND)(nint)(-1);

    /// <summary>Makes the window a no-activate tool window (no taskbar button, not in Alt+Tab) and opts out of DWM corner rounding.</summary>
    public static void ApplyDockStyles(nint hwnd)
    {
        var h = (HWND)hwnd;
        var ex = (nint)PInvoke.GetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        ex = (ex | WsExNoActivate | WsExToolWindow) & ~(nint)WsExAppWindow;
        PInvoke.SetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, ex);

        var pref = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
        PInvoke.DwmSetWindowAttribute(h, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &pref, (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE));
    }

    public static int GetExStyle(nint hwnd) =>
        (int)(nint)PInvoke.GetWindowLongPtr((HWND)hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

    /// <summary>The primary monitor's full bounds (physical pixels) and effective DPI.</summary>
    public static (RectI Bounds, uint Dpi) GetPrimaryMonitor()
    {
        var mon = PInvoke.MonitorFromPoint(new System.Drawing.Point(0, 0), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (!PInvoke.GetMonitorInfo(mon, ref mi)) throw new InvalidOperationException("GetMonitorInfo(primary) failed");
        uint dpi = 96;
        if (OperatingSystem.IsWindowsVersionAtLeast(10) && PInvoke.GetDpiForMonitor(mon, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dx, out _).Succeeded && dx > 0) dpi = dx;
        var r = mi.rcMonitor;
        return (new RectI(r.left, r.top, r.right, r.bottom), dpi);
    }

    /// <summary>
    /// Raw low-level-hook points can lie outside every monitor (the cursor clip is applied after the hook);
    /// pull such points back onto the nearest monitor so "pushing against the bottom edge" reads as the edge row.
    /// </summary>
    public static PointI ClampToNearestMonitor(PointI p)
    {
        var pt = new System.Drawing.Point(p.X, p.Y);
        if (PInvoke.MonitorFromPoint(pt, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL) != HMONITOR.Null) return p;
        var mon = PInvoke.MonitorFromPoint(pt, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (!PInvoke.GetMonitorInfo(mon, ref mi)) return p;
        var r = mi.rcMonitor;
        return new PointI(Math.Clamp(p.X, r.left, r.right - 1), Math.Clamp(p.Y, r.top, r.bottom - 1));
    }

    /// <summary>Moves/sizes the window (physical pixels) and puts it at the top of the topmost band, without activating it.</summary>
    public static void PlaceTopmost(nint hwnd, RectI r) =>
        PInvoke.SetWindowPos((HWND)hwnd, HwndTopmost, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);

    /// <summary>Current cursor position (physical pixels), or null if it can't be read (e.g. secure desktop).</summary>
    public static PointI? GetCursorPos() =>
        PInvoke.GetCursorPos(out var p) ? new PointI(p.X, p.Y) : null;

    /// <summary>True while any of the left / right / middle mouse buttons is physically down.</summary>
    public static bool AnyMouseButtonDown() =>
        (((ushort)PInvoke.GetAsyncKeyState(0x01) | (ushort)PInvoke.GetAsyncKeyState(0x02) | (ushort)PInvoke.GetAsyncKeyState(0x04)) & 0x8000) != 0;

    public static RectI GetWindowRect(nint hwnd) =>
        PInvoke.GetWindowRect((HWND)hwnd, out var r) ? new RectI(r.left, r.top, r.right, r.bottom) : default;
}
