#if DEBUG
using NotWin.Core;
using NotWin.Native;

namespace NotWin.Dock;

/// <summary>
/// Debug-only, opt-in (NOTWIN_DOCK_SELFTEST=1 or =hold) integration check of the reveal path without
/// touching the real cursor: the real mouse hook is not installed; synthetic points go through DockReveal,
/// the machine, the deadline timer and the window's show/hide animation, and the results are logged.
/// "hold" leaves the dock pinned visible afterwards (for screenshots).
/// </summary>
internal static class DockSelfTest
{
    public static string? Mode => Environment.GetEnvironmentVariable("NOTWIN_DOCK_SELFTEST") switch
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

            // 1. Edge held -> shows after 150 ms. Frame timing of the reveal is recorded via CompositionTarget.Rendering.
            var frames = new List<double>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            EventHandler onFrame = (_, _) => frames.Add(clock.Elapsed.TotalMilliseconds);
            System.Windows.Media.CompositionTarget.Rendering += onFrame;
            reveal.OnCursorMoved(edge);
            await Task.Delay(80);
            Check("edge: not yet at 80 ms", !vm.IsVisible);
            await Task.Delay(200);
            Check("edge: visible after 150 ms (no further moves)", vm.IsVisible);
            await Task.Delay(450); // animation 240 ms + margin
            System.Windows.Media.CompositionTarget.Rendering -= onFrame;
            var gaps = frames.Zip(frames.Skip(1), (a, b) => b - a).ToList();
            Log.Info($"selftest reveal frames: count={frames.Count} span={(frames.Count > 1 ? frames[^1] - frames[0] : 0):F0} ms " +
                     $"maxGap={(gaps.Count > 0 ? gaps.Max() : 0):F1} ms avgGap={(gaps.Count > 0 ? gaps.Average() : 0):F1} ms " +
                     $"renderTier={System.Windows.Media.RenderCapability.Tier >> 16} mode={System.Windows.Media.RenderOptions.ProcessRenderMode}");
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
            await Task.Delay(450);
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
            await Task.Delay(1200);
            Check("leave pill: hidden", !vm.IsVisible && !dock.IsVisible);

            // 5. Unclamped point below the screen is clamped onto the edge row.
            var clamped = DockWindowNative.ClampToNearestMonitor(below);
            Check("clamp below-screen point to edge row", clamped == edge || EdgeGeometry.IsAtBottomEdge(clamped, m), $"clamped={clamped}");

            // 6. Full screen: edge does nothing.
            reveal.SetFullscreen(true);
            reveal.OnCursorMoved(edge);
            await Task.Delay(400);
            Check("fullscreen: edge ignored", !vm.IsVisible);
            reveal.SetFullscreen(false, away);
            await Task.Delay(300);
            Check("fullscreen off with cursor away: stays hidden", !vm.IsVisible);

            // 6b. Stale point: cursor was on the edge when full-screen began; it is elsewhere when it ends.
            reveal.OnCursorMoved(edge);
            await Task.Delay(50); // under the 150 ms reveal delay
            reveal.SetFullscreen(true);
            await Task.Delay(300);
            reveal.SetFullscreen(false, away); // fresh GetCursorPos value in the real app
            await Task.Delay(400);
            Check("fullscreen off: stale edge point ignored", !vm.IsVisible && !dock.IsVisible);

            // 7. Keep visible pins it.
            reveal.SetKeepVisible(true);
            await Task.Delay(400);
            Check("keep visible: shown", vm.IsVisible && dock.IsVisible);
            if (!hold)
            {
                reveal.SetKeepVisible(false);
                await Task.Delay(1200);
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
