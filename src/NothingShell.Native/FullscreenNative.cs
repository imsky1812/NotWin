using System.Runtime.InteropServices;
using NothingShell.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Shell;

namespace NothingShell.Native;

/// <summary>Win32 pieces behind FullscreenMonitor: probing the foreground window and installing WinEvent hooks.</summary>
public static unsafe class FullscreenNative
{
    public delegate void EventCallback(uint eventId, nint hwnd, int idObject);

    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectLocationChange = 0x800B;
    private const int ObjIdWindow = 0;

    public static nint GetForeground() => PInvoke.GetForegroundWindow();

    /// <summary>Probes the foreground window against the monitor it is on; null if there is no usable foreground window.</summary>
    public static unsafe FullscreenProbe? Probe()
    {
        var hwnd = PInvoke.GetForegroundWindow();
        if (hwnd == HWND.Null) return null;

        var mon = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!PInvoke.GetMonitorInfo(mon, ref mi)) return null;

        RECT frame;
        unsafe
        {
            var hr = PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &frame, (uint)sizeof(RECT));
            if (hr.Failed && !PInvoke.GetWindowRect(hwnd, out frame)) return null;
        }

        string cls;
        unsafe
        {
            Span<char> buf = stackalloc char[256];
            fixed (char* p = buf)
            {
                int n = PInvoke.GetClassName(hwnd, p, buf.Length);
                cls = new string(p, 0, n);
            }
        }

        bool quns = false;
        if (PInvoke.SHQueryUserNotificationState(out var state).Succeeded)
            quns = state is QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                or QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
                or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;

        return new FullscreenProbe(
            new RectI(frame.left, frame.top, frame.right, frame.bottom),
            new RectI(mi.rcMonitor.left, mi.rcMonitor.top, mi.rcMonitor.right, mi.rcMonitor.bottom),
            cls, PInvoke.IsZoomed(hwnd), quns);
    }

    /// <summary>
    /// Installs out-of-context hooks for foreground changes and window location changes. Must be called on a
    /// thread with a message loop. The caller must keep the returned handle (and the callback) alive.
    /// </summary>
    public sealed class Hook : IDisposable
    {
        // Function pointers cannot capture, so the managed callback lives in a static slot (one hook at a time).
        private static EventCallback? s_callback;
        private HWINEVENTHOOK _fg, _loc;

        public unsafe Hook(EventCallback callback)
        {
            s_callback = callback;
            const uint outOfContext = 0x0000, skipOwnProcess = 0x0002;
            _fg = PInvoke.SetWinEventHook(EventSystemForeground, EventSystemForeground, HMODULE.Null, &Proc, 0, 0, outOfContext | skipOwnProcess);
            _loc = PInvoke.SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, HMODULE.Null, &Proc, 0, 0, outOfContext | skipOwnProcess);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
        private static void Proc(HWINEVENTHOOK hook, uint ev, HWND hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try { s_callback?.Invoke(ev, hwnd, idObject); } catch { /* never throw across the native boundary */ }
        }

        public void Dispose()
        {
            if (_fg != default) PInvoke.UnhookWinEvent(_fg);
            if (_loc != default) PInvoke.UnhookWinEvent(_loc);
            _fg = default; _loc = default;
            s_callback = null;
        }
    }

    public const uint ForegroundEvent = EventSystemForeground;
    public const uint LocationChangeEvent = EventObjectLocationChange;
    public const int WindowObjectId = ObjIdWindow;
}
