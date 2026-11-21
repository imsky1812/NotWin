using Windows.Win32;
using Windows.Win32.Foundation;

namespace NothingShell.Native;

/// <summary>Bringing a window to the foreground when Windows' foreground lock would refuse a plain SetForegroundWindow.</summary>
public static unsafe class ForegroundNative
{
    /// <summary>
    /// Makes <paramref name="hwnd"/> the foreground window with keyboard focus. Tries SetForegroundWindow first;
    /// if another window still owns the foreground, briefly attaches to that window's input thread (the standard
    /// workaround) and tries again. Returns whether <paramref name="hwnd"/> ended up in front.
    /// </summary>
    public static bool Activate(nint hwnd)
    {
        var target = new HWND(hwnd);
        PInvoke.SetForegroundWindow(target);
        if (PInvoke.GetForegroundWindow() == target) return true;

        var fg = PInvoke.GetForegroundWindow();
        var fgThread = fg.IsNull ? 0u : PInvoke.GetWindowThreadProcessId(fg, null);
        var ours = PInvoke.GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != ours && PInvoke.AttachThreadInput(ours, fgThread, true);
        try
        {
            PInvoke.BringWindowToTop(target);
            PInvoke.SetForegroundWindow(target);
            PInvoke.SetFocus(target);
        }
        finally
        {
            if (attached) PInvoke.AttachThreadInput(ours, fgThread, false);
        }
        return PInvoke.GetForegroundWindow() == target;
    }
}
