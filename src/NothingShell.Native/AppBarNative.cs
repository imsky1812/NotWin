using System.Runtime.InteropServices;
using NothingShell.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace NothingShell.Native;

/// <summary>SHAppBarMessage wrappers for registering the dock as a bottom appbar (reserved work-area strip).</summary>
public static class AppBarNative
{
    private const uint AbmNew = 0;
    private const uint AbmRemove = 1;
    private const uint AbmQueryPos = 2;
    private const uint AbmSetPos = 3;
    private const uint AbeBottom = 3;

    /// <summary>wParam of the callback message when another appbar / the taskbar changed the layout.</summary>
    public const int AbnPosChanged = 1;

    /// <summary>A window message id private to this process for appbar notifications.</summary>
    public static uint RegisterCallbackMessage() => RegisterWindowMessage("NothingShellAppBar");

    public static bool New(nint hwnd, uint callbackMessage)
    {
        var d = Data(hwnd);
        d.uCallbackMessage = callbackMessage;
        return PInvoke.SHAppBarMessage(AbmNew, ref d) != 0;
    }

    public static void Remove(nint hwnd)
    {
        var d = Data(hwnd);
        PInvoke.SHAppBarMessage(AbmRemove, ref d);
    }

    /// <summary>
    /// Claims the bottom <paramref name="heightPx"/> pixels of <paramref name="monitor"/> (query, then set).
    /// Returns the rectangle the system granted.
    /// </summary>
    public static RectI SetBottom(nint hwnd, RectI monitor, int heightPx, RectI? current = null)
    {
        var d = Data(hwnd);
        d.uEdge = AbeBottom;
        d.rc = new RECT { left = monitor.Left, top = monitor.Bottom - heightPx, right = monitor.Right, bottom = monitor.Bottom };
        PInvoke.SHAppBarMessage(AbmQueryPos, ref d);
        d.rc.top = d.rc.bottom - heightPx; // QUERYPOS may move the bottom edge up (other bottom appbars); keep our height
        var queried = new RectI(d.rc.left, d.rc.top, d.rc.right, d.rc.bottom);
        if (queried == current) return queried; // unchanged: skip SETPOS so it can't ping-pong ABN_POSCHANGED
        PInvoke.SHAppBarMessage(AbmSetPos, ref d);
        return new RectI(d.rc.left, d.rc.top, d.rc.right, d.rc.bottom);
    }

    private static APPBARDATA Data(nint hwnd) => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = (HWND)hwnd,
    };

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessage(string name);
}
