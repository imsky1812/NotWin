using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NothingShell.Native;

/// <summary>Low-level (WH_KEYBOARD_LL) keyboard hook plus the "mask key" used to stop a lone Win from opening Start.</summary>
public static unsafe class KeyboardHookNative
{
    /// <summary>dwExtraInfo on keys we inject ("NSH1"), so our own hook can recognise and ignore them.</summary>
    public const nuint OurMarker = 0x4E534831;

    /// <summary>VK 0xE8 is unassigned: pressing it between Win down and Win up makes Windows treat Win as a combo.</summary>
    private const ushort MaskVk = 0xE8;

    /// <summary>Called on the hook thread for every key event. Return true to swallow the key. Must return fast.</summary>
    public delegate bool KeyCallback(uint vk, bool down, bool injectedByUs);

    /// <summary>Sends the mask key down + up, marked with <see cref="OurMarker"/>. Safe to call from inside the hook.</summary>
    public static void InjectMaskKey()
    {
        var inputs = stackalloc INPUT[2];
        for (var i = 0; i < 2; i++)
        {
            inputs[i] = default;
            inputs[i].type = INPUT_TYPE.INPUT_KEYBOARD;
            inputs[i].Anonymous.ki.wVk = (VIRTUAL_KEY)MaskVk;
            inputs[i].Anonymous.ki.dwExtraInfo = OurMarker;
        }
        inputs[1].Anonymous.ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        PInvoke.SendInput(2, inputs, sizeof(INPUT));
    }

    /// <summary>True while the key is physically down (GetAsyncKeyState high bit).</summary>
    public static bool IsPhysicallyDown(uint vk) => (PInvoke.GetAsyncKeyState((int)vk) & 0x8000) != 0;

    /// <summary>
    /// Installs the hook on <paramref name="thread"/> (a pumping <see cref="HookThread"/>). One instance at a time:
    /// the callback lives in a static slot because the native entry point is a static function pointer.
    /// </summary>
    public sealed class Hook : IDisposable
    {
        private const uint LlkhfInjected = 0x10;
        private static KeyCallback? s_callback;
        private readonly HookThread _thread;
        private HHOOK _hook;

        public Hook(HookThread thread, KeyCallback callback)
        {
            if (s_callback is not null) throw new InvalidOperationException("Only one keyboard hook may be installed at a time.");
            _thread = thread;
            s_callback = callback;
            var error = 0;
            thread.Invoke(() =>
            {
                var module = PInvoke.GetModuleHandle((char*)null);
                _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, &Proc, (HINSTANCE)(nint)module, 0);
                if (_hook == default) error = Marshal.GetLastWin32Error();
            });
            if (_hook == default)
            {
                s_callback = null;
                throw new Win32Exception(error, "SetWindowsHookEx(WH_KEYBOARD_LL) failed");
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static LRESULT Proc(int code, WPARAM wParam, LPARAM lParam)
        {
            if (code >= 0)
            {
                var info = (KBDLLHOOKSTRUCT*)lParam.Value;
                var msg = (uint)wParam.Value;
                var down = msg is 0x0100 /* WM_KEYDOWN */ or 0x0104 /* WM_SYSKEYDOWN */;
                var injectedByUs = ((uint)info->flags & LlkhfInjected) != 0 && info->dwExtraInfo == OurMarker;
                try
                {
                    if (s_callback?.Invoke(info->vkCode, down, injectedByUs) == true) return (LRESULT)1;
                }
                catch { /* never throw across the native boundary */ }
            }
            return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
        }

        public void Dispose()
        {
            var h = _hook;
            _hook = default;
            if (h != default)
            {
                try { _thread.Invoke(() => PInvoke.UnhookWindowsHookEx(h)); }
                catch (Exception) { PInvoke.UnhookWindowsHookEx(h); } // thread gone: unhook from here as a last resort
            }
            s_callback = null;
        }
    }
}
