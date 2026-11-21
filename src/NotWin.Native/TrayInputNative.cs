using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace NotWin.Native;

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

    // The last foreground window that was NOT the explorer tray: where focus goes back to after the flyout.
    private static nint s_previous;

    /// <summary>
    /// Opens Windows' own tray-overflow flyout while the taskbar is hidden: Win+B (focus "Show hidden icons" on the
    /// tray), then Enter. Enter is sent only when Win+B demonstrably worked: the foreground must CHANGE from a
    /// non-tray window to the explorer-owned Shell_TrayWnd after Win+B. If the foreground is already the tray beforehand
    /// (left there by a previous flyout), focus is first handed back to the last real window; if that is not possible
    /// the open is refused. Blocks for up to ~1.5 s: call it off the UI thread. Never sends Esc or anything else.
    /// <paramref name="detail"/> describes what was seen (for the log).
    /// </summary>
    public static OverflowResult OpenOverflow(out long flyoutMs, out string detail)
    {
        flyoutMs = -1;
        detail = "";
        if (!TaskbarNative.HasTray()) return OverflowResult.NoTray;

        var before = (nint)PInvoke.GetForegroundWindow();
        if (TaskbarNative.IsExplorerTrayWindow(before))
        {
            // Stale tray focus: a "tray is foreground" check would pass before Win+B is even processed.
            var prev = s_previous;
            if (prev != 0 && PInvoke.IsWindow(new HWND(prev))) PInvoke.SetForegroundWindow(new HWND(prev));
            var neutral = false;
            for (var i = 0; i < 30; i++)
            {
                Thread.Sleep(10);
                if (!TaskbarNative.IsExplorerTrayWindow(PInvoke.GetForegroundWindow())) { neutral = true; break; }
            }
            if (!neutral) { detail = "foreground already the tray and could not be moved; refused"; return OverflowResult.FocusNotOnTray; }
            before = (nint)PInvoke.GetForegroundWindow();
            detail = "stale tray focus cleared; ";
        }
        if (before != 0) s_previous = before;
        detail += $"before={ClassOf(before)}; ";

        SendChord(VIRTUAL_KEY.VK_LWIN, VIRTUAL_KEY.VK_B);

        var focused = false;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 600)
        {
            var fg = (nint)PInvoke.GetForegroundWindow();
            if (fg != before && TaskbarNative.IsExplorerTrayWindow(fg)) { focused = true; break; }
            Thread.Sleep(10);
        }
        if (!focused) { detail += "foreground never changed to the tray; Enter NOT sent"; return OverflowResult.FocusNotOnTray; }
        detail += $"foreground changed to tray after {sw.ElapsedMilliseconds} ms; ";

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

    /// <summary>
    /// After the flyout opened: waits (cheap 200 ms polls, up to 2 minutes) for it to close, then, if focus was left on the
    /// explorer tray, hands it back to the window that had it before. Returns true when focus was restored.
    /// </summary>
    public static bool RestoreFocusAfterFlyout()
    {
        var sw = Stopwatch.StartNew();
        while (FlyoutVisible() && sw.Elapsed < TimeSpan.FromMinutes(2)) Thread.Sleep(200);
        var prev = s_previous;
        if (prev == 0 || !PInvoke.IsWindow(new HWND(prev))) return false;
        // After the flyout closes the shell moves focus to the (hidden) tray a moment later: wait for it to settle.
        for (var i = 0; i < 100; i++)
        {
            var fg = PInvoke.GetForegroundWindow();
            if (TaskbarNative.IsExplorerTrayWindow(fg)) { AllowForeground(); return PInvoke.SetForegroundWindow(new HWND(prev)); }
            var settledElsewhere = fg != HWND.Null && ClassOf(fg) != OverflowClass;
            if (settledElsewhere && i > 20) return false; // the user already went to another window
            Thread.Sleep(10);
        }
        return false;
    }

    // SetForegroundWindow is only honoured for the process that last received input: a zero-distance marked mouse move
    // from us makes that true without any visible effect.
    private static void AllowForeground()
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_MOUSE };
        input.Anonymous.mi = new MOUSEINPUT { dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE, dwExtraInfo = Mark };
        Span<INPUT> one = [input];
        Send(one);
    }

    private static string ClassOf(nint hwnd)
    {
        Span<char> cls = stackalloc char[64];
        var n = PInvoke.GetClassName(new HWND(hwnd), cls);
        return n > 0 ? new string(cls[..n]) : "none";
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
