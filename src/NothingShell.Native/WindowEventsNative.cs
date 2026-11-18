using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace NothingShell.Native;

/// <summary>
/// Out-of-context WinEvent hooks for window lifecycle: create, destroy, show, hide, name change, cloak/uncloak
/// and foreground changes. Own-process events are skipped. Must be created and disposed on a thread with a
/// message loop (the WPF UI thread). The callback only receives events for whole windows (OBJID_WINDOW) and
/// must stay tiny: no enumeration in it.
/// </summary>
public sealed unsafe class WindowEventsHook : IDisposable
{
    // Function pointers cannot capture, so the managed callback lives in a static slot (one instance at a time).
    private static Action? s_callback;
    private readonly HWINEVENTHOOK[] _hooks;

    public WindowEventsHook(Action onEvent)
    {
        s_callback = onEvent;
        const uint outOfContext = 0x0000, skipOwnProcess = 0x0002;
        const uint flags = outOfContext | skipOwnProcess;
        // EVENT_SYSTEM_FOREGROUND; EVENT_OBJECT_CREATE..HIDE; NAMECHANGE; CLOAKED..UNCLOAKED
        (uint Min, uint Max)[] ranges = [(0x0003, 0x0003), (0x8000, 0x8003), (0x800C, 0x800C), (0x8017, 0x8018)];
        _hooks = new HWINEVENTHOOK[ranges.Length];
        for (var i = 0; i < ranges.Length; i++)
            _hooks[i] = PInvoke.SetWinEventHook(ranges[i].Min, ranges[i].Max, HMODULE.Null, &Proc, 0, 0, flags);
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void Proc(HWINEVENTHOOK hook, uint ev, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 /* OBJID_WINDOW */ || idChild != 0 || hwnd == HWND.Null) return;
        try { s_callback?.Invoke(); } catch { /* never throw across the native boundary */ }
    }

    public void Dispose()
    {
        foreach (var h in _hooks)
            if (h != default) PInvoke.UnhookWinEvent(h);
        s_callback = null;
    }
}
