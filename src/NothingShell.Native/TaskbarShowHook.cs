using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace NothingShell.Native;

/// <summary>
/// Fires when Explorer shows one of its taskbar windows again. Explorer re-shows Shell_TrayWnd on its own
/// after a full-screen window (video, game, the screenshot overlay) goes away, which would put the Windows
/// taskbar back behind the dock. Out-of-context EVENT_OBJECT_SHOW hook: create and dispose on the UI thread.
/// One instance at a time. The callback must stay tiny (it runs inside the hook); queue real work.
/// </summary>
public sealed unsafe class TaskbarShowHook : IDisposable
{
    private const uint EventObjectShow = 0x8002;
    private static Action? s_callback;
    private HWINEVENTHOOK _hook;

    public TaskbarShowHook(Action onTaskbarShown)
    {
        s_callback = onTaskbarShown;
        const uint outOfContext = 0x0000, skipOwnProcess = 0x0002;
        _hook = PInvoke.SetWinEventHook(EventObjectShow, EventObjectShow, HMODULE.Null, &Proc, 0, 0, outOfContext | skipOwnProcess);
    }

    /// <summary>False when SetWinEventHook failed.</summary>
    public bool Installed => _hook != default;

    [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void Proc(HWINEVENTHOOK hook, uint ev, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 /* OBJID_WINDOW */ || idChild != 0 || hwnd == HWND.Null) return;
        Span<char> cls = stackalloc char[32];
        var n = PInvoke.GetClassName(hwnd, cls);
        if (n <= 0) return;
        var name = cls[..n];
        if (!name.SequenceEqual("Shell_TrayWnd") && !name.SequenceEqual("Shell_SecondaryTrayWnd")) return;
        try { s_callback?.Invoke(); } catch { /* never throw across the native boundary */ }
    }

    public void Dispose()
    {
        if (_hook != default) PInvoke.UnhookWinEvent(_hook);
        _hook = default;
        s_callback = null;
    }
}
