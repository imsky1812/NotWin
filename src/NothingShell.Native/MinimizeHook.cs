using System.Runtime.InteropServices;

namespace NothingShell.Native;

/// <summary>
/// Minimize start / end events of other processes' windows (EVENT_SYSTEM_MINIMIZESTART 0x0016 / MINIMIZEEND 0x0017),
/// out of context, raised on the creating (UI) thread's message loop. Used by the dot drain (Plan 7).
/// </summary>
public sealed class MinimizeHook : IDisposable
{
    private delegate void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);
    private readonly WinEventProc _proc; // kept alive for as long as the hook exists
    private nint _hook;

    /// <param name="onMinimize">The window and true when it starts minimizing, false when it is restored.</param>
    public MinimizeHook(Action<nint, bool> onMinimize)
    {
        _proc = (_, ev, hwnd, idObject, _, _, _) =>
        {
            if (idObject != 0 || hwnd == 0) return; // OBJID_WINDOW only
            try { onMinimize(hwnd, ev == 0x0016); }
            catch { /* never throw across the native boundary */ }
        };
        _hook = SetWinEventHook(0x0016, 0x0017, 0, _proc, 0, 0, 0x0000 | 0x0002 /* OUTOFCONTEXT | SKIPOWNPROCESS */);
    }

    public bool Installed => _hook != 0;

    public void Dispose()
    {
        if (_hook == 0) return;
        UnhookWinEvent(_hook);
        _hook = 0;
    }

    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
}
