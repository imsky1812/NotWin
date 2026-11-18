#if DEBUG
using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Dock;

/// <summary>
/// Debug-only, opt-in (NOTHINGSHELL_DOCK_SELFTEST=1 or =hold) integration check of the reveal path without
/// touching the real cursor: the real mouse hook is not installed; synthetic points go through DockReveal,
/// the machine, the deadline timer and the window's show/hide animation, and the results are logged.
/// "hold" leaves the dock pinned visible afterwards (for screenshots).
/// </summary>
internal static class DockSelfTest
{
    public static string? Mode => Environment.GetEnvironmentVariable("NOTHINGSHELL_DOCK_SELFTEST") switch
    {
        "1" => "run",
        "hold" => "hold",
        _ => null,
    };

    public static async void Run(DockWindow dock, DockReveal reveal, DockViewModel vm, bool hold)
    {
        int passed = 0, total = 0;
        void Check(string name, bool ok, string detail = "")
        {
            total++;
            if (ok) passed++;
            Log.Info($"selftest {(ok ? "PASS" : "FAIL")} {name} {detail}");
        }

        try
        {
            await Task.Delay(500);
            var m = dock.Placement.Monitor;
            var edge = new PointI((m.Left + m.Right) / 2, m.Bottom - 1);
            var away = new PointI((m.Left + m.Right) / 2, (m.Top + m.Bottom) / 2);
            var below = new PointI(edge.X, m.Bottom + 5); // raw LL points can be past the clip
            Check("starts hidden", !vm.IsVisible && !dock.IsVisible);

            // 1. Edge held -> shows after 150 ms.
            reveal.OnCursorMoved(edge);
            await Task.Delay(80);
            Check("edge: not yet at 80 ms", !vm.IsVisible);
            await Task.Delay(200);
            Check("edge: visible after 150 ms (no further moves)", vm.IsVisible);
            await Task.Delay(350); // animation 240 ms
            var ex = DockWindowNative.GetExStyle(dock.Handle);
            var rect = DockWindowNative.GetWindowRect(dock.Handle);
            Check("window shown", dock.IsVisible);
            Check("window rect == placement", rect == dock.Placement.Window, $"rect={rect} placement={dock.Placement}");
            Check("ex styles", (ex & DockWindowNative.WsExNoActivate) != 0 && (ex & DockWindowNative.WsExToolWindow) != 0 &&
                               (ex & DockWindowNative.WsExLayered) != 0 && (ex & DockWindowNative.WsExTopmost) != 0 &&
                               (ex & DockWindowNative.WsExAppWindow) == 0, $"exstyle=0x{ex:X8}");
            Check("pill animation settled", Math.Abs(dock.Pill.Opacity - 1) < 0.001 && Math.Abs(dock.Slide.Y) < 0.001,
                  $"opacity={dock.Pill.Opacity} y={dock.Slide.Y}");

            // 2. Away -> hides 500 ms later.
            reveal.OnCursorMoved(away);
            await Task.Delay(350);
            Check("away: still visible at 350 ms", vm.IsVisible);
            await Task.Delay(300);
            Check("away: hidden after 500 ms", !vm.IsVisible);
            await Task.Delay(350);
            Check("window hidden after animation", !dock.IsVisible);

            // 3. Flick past the edge -> stays hidden.
            reveal.OnCursorMoved(edge);
            await Task.Delay(40);
            reveal.OnCursorMoved(away);
            await Task.Delay(300);
            Check("flick: stays hidden", !vm.IsVisible && !dock.IsVisible);

            // 4. Over the pill keeps it visible; leaving hides.
            reveal.OnCursorMoved(edge);
            await Task.Delay(250);
            var p = dock.Placement.Pill;
            reveal.OnCursorMoved(new PointI((p.Left + p.Right) / 2, (p.Top + p.Bottom) / 2));
            await Task.Delay(800);
            Check("over pill: stays visible", vm.IsVisible);
            reveal.OnCursorMoved(away);
            await Task.Delay(900);
            Check("leave pill: hidden", !vm.IsVisible && !dock.IsVisible);

            // 5. Unclamped point below the screen is clamped onto the edge row.
            var clamped = DockWindowNative.ClampToNearestMonitor(below);
            Check("clamp below-screen point to edge row", clamped == edge || EdgeGeometry.IsAtBottomEdge(clamped, m), $"clamped={clamped}");

            // 6. Full screen: edge does nothing.
            reveal.SetFullscreen(true);
            reveal.OnCursorMoved(edge);
            await Task.Delay(400);
            Check("fullscreen: edge ignored", !vm.IsVisible);
            reveal.OnCursorMoved(away);
            reveal.SetFullscreen(false);

            // 7. Keep visible pins it.
            reveal.SetKeepVisible(true);
            await Task.Delay(400);
            Check("keep visible: shown", vm.IsVisible && dock.IsVisible);
            if (!hold)
            {
                reveal.SetKeepVisible(false);
                await Task.Delay(900);
                Check("keep visible off: hidden", !vm.IsVisible && !dock.IsVisible);
            }

            Log.Info($"selftest result: {passed}/{total} passed{(hold ? " (holding dock visible)" : "")}");
        }
        catch (Exception e)
        {
            Log.Error("selftest crashed", e);
        }
    }
}
#endif
