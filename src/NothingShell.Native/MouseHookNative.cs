using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NothingShell.Native;

/// <summary>Low-level (WH_MOUSE_LL) mouse hook that reports raw move points.</summary>
public static unsafe class MouseHookNative
{
    /// <summary>Called on the installing thread for every mouse move. Must return quickly (Windows drops slow LL hooks).</summary>
    public delegate void MoveCallback(int x, int y);

    private const uint WmMouseMove = 0x0200;

    /// <summary>
    /// Installs the hook on <paramref name="thread"/> (a pumping <see cref="HookThread"/>, not the UI thread, so a busy
    /// UI can never make Windows drop the hook). The callback runs on that thread.
    /// One instance at a time: the callback lives in a static slot because the native entry point is a
    /// static function pointer (nothing for the GC to collect).
    /// </summary>
    public sealed class Hook : IDisposable
    {
        private static MoveCallback? s_callback;
        private readonly HookThread _thread;
        private HHOOK _hook;

        public Hook(HookThread thread, MoveCallback callback)
        {
            if (s_callback is not null) throw new InvalidOperationException("Only one mouse hook may be installed at a time.");
            _thread = thread;
            s_callback = callback;
            var error = 0;
            thread.Invoke(() =>
            {
                var module = PInvoke.GetModuleHandle((char*)null);
                _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, &Proc, (HINSTANCE)(nint)module, 0);
                if (_hook == default) error = Marshal.GetLastWin32Error();
            });
            if (_hook == default)
            {
                s_callback = null;
                throw new Win32Exception(error, "SetWindowsHookEx(WH_MOUSE_LL) failed");
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static LRESULT Proc(int code, WPARAM wParam, LPARAM lParam)
        {
            if (code >= 0 && wParam.Value == WmMouseMove)
            {
                var info = (MSLLHOOKSTRUCT*)lParam.Value;
                try { s_callback?.Invoke(info->pt.X, info->pt.Y); }
                catch { /* never throw across the native boundary */ }
            }
            return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
        }

        public void Dispose()
        {
            var h = _hook;
            _hook = default;
            s_callback = null; // first: a callback already in flight on the hook thread then does nothing
            if (h == default) return;
            try { _thread.Invoke(() => PInvoke.UnhookWindowsHookEx(h)); }
            catch (Exception) { PInvoke.UnhookWindowsHookEx(h); } // thread gone: unhook from here as a last resort
        }
    }
}
