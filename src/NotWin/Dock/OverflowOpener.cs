using NotWin.Native;

namespace NotWin.Dock;

/// <summary>
/// Opens Windows' own tray-overflow flyout (Discord, Steam, ... with their real right-click menus).
/// Strategy 3 from the Phase 0 spike: with the taskbar hidden, Win+B then Enter; Enter is sent only if Win+B put focus on
/// the explorer-owned Shell_TrayWnd (see <see cref="TrayInputNative.OpenOverflow"/>), so a stray Enter can never reach
/// another app. Strategy 2 (show the bar, UIA-invoke the chevron, hide) is deliberately not included: it needs
/// UIAutomation (a large assembly set) and the spike never needed it. If Win+B fails at runtime nothing is opened and
/// the reason is logged.
/// </summary>
public static class OverflowOpener
{
    private static int _busy;

    /// <summary>Starts the open on a worker thread (the key sequence waits on the shell); returns immediately.</summary>
    public static void Open()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return; // a previous open is still running
        Task.Run(() =>
        {
            try
            {
                var result = TrayInputNative.OpenOverflow(out var ms, out var detail);
                Log.Info($"overflow: {result}{(ms >= 0 ? $" (flyout {ms} ms after Enter)" : "")} [{detail}]");
                // Win+B can reveal the explorer bar; put it back (the flyout survives the hide, per the spike).
                TrayInputNative.RehideTaskbarIfShown();
                Thread.Sleep(500);
                TrayInputNative.RehideTaskbarIfShown();
                if (result == OverflowResult.Opened)
                    Log.Info($"overflow closed; focus restored to previous window: {TrayInputNative.RestoreFocusAfterFlyout()}");
            }
            catch (Exception e) { Log.Error("overflow open failed", e); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        });
    }
}
