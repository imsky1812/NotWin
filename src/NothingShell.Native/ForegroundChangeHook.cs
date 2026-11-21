using System.Runtime.InteropServices;

namespace NothingShell.Native;

/// <summary>
/// A small out-of-context WinEvent hook for EVENT_SYSTEM_FOREGROUND only, raised on the creating (UI) thread's message
/// loop. The snake widget holds one only while it has the keys, so "focus moved elsewhere" pauses it (spec §6.5).
/// </summary>
public sealed class ForegroundChangeHook : IDisposable
{
    private delegate void WinEventProc(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time);
    private readonly WinEventProc _proc; // kept alive for as long as the hook exists
    private nint _hook;

    public ForegroundChangeHook(Action<nint> onForeground)
    {
        _proc = (_, _, hwnd, idObject, _, _, _) =>
        {
            if (idObject != 0) return; // OBJID_WINDOW only
            try { onForeground(hwnd); }
            catch { /* never throw across the native boundary */ }
        };
        _hook = SetWinEventHook(0x0003, 0x0003, 0, _proc, 0, 0, 0 /* WINEVENT_OUTOFCONTEXT */);
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
