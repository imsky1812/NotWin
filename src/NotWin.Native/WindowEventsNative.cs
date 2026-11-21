using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NotWin.Native;

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
    // Handles of the windows the tracker currently lists. Published as an immutable-by-convention set
    // reference (never mutated after publishing), so the callback can read it without a lock.
    private static volatile HashSet<nint> s_tracked = [];
    private readonly HWINEVENTHOOK[] _hooks;

    /// <summary>Publishes the set of listed top-level windows; their HIDE/DESTROY events always get through.</summary>
    public void SetTracked(IEnumerable<nint> handles) => s_tracked = new HashSet<nint>(handles);

    /// <summary>How many of the hook ranges failed to install (0 when healthy).</summary>
    public int FailedHooks { get; }

    public WindowEventsHook(Action onEvent)
    {
        s_callback = onEvent;
        const uint outOfContext = 0x0000, skipOwnProcess = 0x0002;
        const uint flags = outOfContext | skipOwnProcess;
        // EVENT_SYSTEM_FOREGROUND; EVENT_OBJECT_CREATE..HIDE; NAMECHANGE; CLOAKED..UNCLOAKED
        (uint Min, uint Max)[] ranges = [(0x0003, 0x0003), (0x8000, 0x8003), (0x800C, 0x800C), (0x8017, 0x8018)];
        _hooks = new HWINEVENTHOOK[ranges.Length];
        for (var i = 0; i < ranges.Length; i++)
        {
            _hooks[i] = PInvoke.SetWinEventHook(ranges[i].Min, ranges[i].Max, HMODULE.Null, &Proc, 0, 0, flags);
            if (_hooks[i] == default) FailedHooks++;
        }
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void Proc(HWINEVENTHOOK hook, uint ev, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 /* OBJID_WINDOW */ || idChild != 0 || hwnd == HWND.Null) return;
        // Object events fire for every child control too (terminal titles, tooltips, menus): only top-level
        // windows matter. Foreground (0x0003) and cloak (0x8017/8) events are already top-level.
        if (ev is >= 0x8000 and <= 0x800C)
        {
            // The callback is out-of-context, so a closing window is usually already gone and GetAncestor
            // can no longer say it was top-level. HIDE/DESTROY therefore pass for any window we list
            // (only those matter for removal); CREATE/SHOW/NAMECHANGE need a live top-level window.
            var removal = ev is 0x8001 or 0x8003;
            if (removal ? !s_tracked.Contains(hwnd)
                        : PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_PARENT) != PInvoke.GetDesktopWindow()) return;
        }
        try { s_callback?.Invoke(); } catch { /* never throw across the native boundary */ }
    }

    public void Dispose()
    {
        foreach (var h in _hooks)
            if (h != default) PInvoke.UnhookWinEvent(h);
        s_callback = null;
        s_tracked = [];
    }
}
