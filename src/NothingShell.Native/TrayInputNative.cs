using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace NothingShell.Native;

/// <summary>Why <see cref="TrayInputNative.OpenOverflow"/> did or did not open the flyout.</summary>
public enum OverflowResult
{
    /// <summary>Win+B focused the explorer tray, Enter was sent and Windows' overflow flyout appeared.</summary>
    Opened,
    /// <summary>Enter was sent but no flyout was seen within the wait.</summary>
    EnterSentNoFlyout,
    /// <summary>No explorer-owned taskbar exists (Explorer restarting).</summary>
    NoTray,
    /// <summary>Win+B did not put focus on the explorer tray: Enter was NOT sent (it could have gone to another app).</summary>
    FocusNotOnTray,
}

/// <summary>
/// Synthetic keyboard input for the tray. Every event carries <see cref="Mark"/> in dwExtraInfo so our own hooks
/// (and anyone watching) can tell it from real typing.
/// </summary>
public static unsafe class TrayInputNative
{
    /// <summary>dwExtraInfo on everything we inject ("NW").</summary>
    public const nuint Mark = 0x4E57;

    private const string OverflowClass = "TopLevelWindowForOverflowXamlIsland";

    /// <summary>Win+A (Windows quick settings).</summary>
    public static bool SendQuickSettings() => SendChord(VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_A);

    /// <summary>
    /// Opens Windows' own tray-overflow flyout while the taskbar is hidden: Win+B (focus "Show hidden icons" on the
    /// tray), wait for focus to land on the explorer-owned Shell_TrayWnd, then Enter. Enter is only sent when that
    /// focus check passes. Blocks for up to ~1.5 s: call it off the UI thread. Never sends Esc or anything else.
    /// </summary>
    public static OverflowResult OpenOverflow(out long flyoutMs)
    {
        flyoutMs = -1;
        if (!TaskbarNative.HasTray()) return OverflowResult.NoTray;

        SendChord(VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_B);

        var focused = false;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 600)
        {
            if (TaskbarNative.IsExplorerTrayWindow(PInvoke.GetForegroundWindow())) { focused = true; break; }
            Thread.Sleep(10);
        }
        if (!focused) return OverflowResult.FocusNotOnTray;

        Thread.Sleep(30); // let the tray settle its keyboard focus on the chevron
        // Re-check right before Enter: the foreground must still be the tray.
        if (!TaskbarNative.IsExplorerTrayWindow(PInvoke.GetForegroundWindow())) return OverflowResult.FocusNotOnTray;
        SendKey(VIRTUAL_KEY.VK_RETURN);

        sw.Restart();
        while (sw.ElapsedMilliseconds < 1000)
        {
            if (FlyoutVisible()) { flyoutMs = sw.ElapsedMilliseconds; return OverflowResult.Opened; }
            Thread.Sleep(10);
        }
        return OverflowResult.EnterSentNoFlyout;
    }

    /// <summary>Win+B reveals the (explorer) bar; put it back the way the shell had it. Safe to call any time.</summary>
    public static void RehideTaskbarIfShown()
    {
        if (TaskbarNative.IsPrimaryVisible()) TaskbarNative.HideAll();
    }

    private static bool FlyoutVisible()
    {
        var h = PInvoke.FindWindowEx(HWND.Null, HWND.Null, OverflowClass, null);
        return h != HWND.Null && PInvoke.IsWindowVisible(h);
    }

    private static bool SendChord(VIRTUAL_KEY modifier, VIRTUAL_KEY key)
    {
        Span<INPUT> inputs =
        [
            Key(modifier, up: false), Key(key, up: false), Key(key, up: true), Key(modifier, up: true),
        ];
        return Send(inputs);
    }

    private static bool SendKey(VIRTUAL_KEY key)
    {
        Span<INPUT> inputs = [Key(key, up: false), Key(key, up: true)];
        return Send(inputs);
    }

    private static bool Send(ReadOnlySpan<INPUT> inputs) =>
        PInvoke.SendInput(inputs, sizeof(INPUT)) == (uint)inputs.Length;

    private static INPUT Key(VIRTUAL_KEY vk, bool up)
    {
        var flags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
        if (vk == VIRTUAL_KEY.VK_LWIN) flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        input.Anonymous.ki = new KEYBDINPUT { wVk = vk, dwFlags = flags, dwExtraInfo = Mark };
        return input;
    }
}
