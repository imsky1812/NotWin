// THROWAWAY spike: can we open Windows' own tray overflow flyout while the taskbar is hidden?
//   OverflowSpike --dump      dump the UIA tree of Shell_TrayWnd (find the chevron)
//   OverflowSpike --run       try strategies 1..3; always restores the taskbar to its initial state
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

static class P
{
    const int SW_HIDE = 0, SW_SHOWNA = 8;
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32")] static extern IntPtr FindWindow(string c, string? t);
    [DllImport("user32")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32")] static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32")] static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("dwmapi")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct KI { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KI ki; [FieldOffset(32)] long pad; }

    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    // Fenn (and possibly other shell replacers) registers its own Shell_TrayWnd, so FindWindow can return the wrong one.
    static IntPtr FindExplorerTray()
    {
        IntPtr found = 0;
        EnumWindows((h, _) =>
        {
            if (Cls(h) != "Shell_TrayWnd") return true;
            GetWindowThreadProcessId(h, out var pid);
            string name; try { name = Process.GetProcessById((int)pid).ProcessName; } catch { return true; }
            Say($"  Shell_TrayWnd hwnd={h} owner={name}({pid}) visible={IsWindowVisible(h)}");
            if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) found = h;
            return true;
        }, 0);
        return found;
    }

    [DllImport("user32", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string c, string? t);
    // Show the tray and (experiment) its XAML bridge child, which stays hidden while Fenn is running.
    static void PinShow(IntPtr tray)
    {
        ShowWindow(tray, SW_SHOWNA);
        var bridge = FindWindowEx(tray, 0, "Windows.UI.Composition.DesktopWindowContentBridge", null);
        if (bridge != 0) ShowWindow(bridge, SW_SHOWNA);
    }

    static void Say(string s) => Console.WriteLine(s);

    static string Cls(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    static bool Cloaked(IntPtr h) => DwmGetWindowAttribute(h, 14, out var v, 4) == 0 && v != 0;

    static Dictionary<IntPtr, string> VisibleTop()
    {
        var d = new Dictionary<IntPtr, string>();
        EnumWindows((h, _) => { if (IsWindowVisible(h) && !Cloaked(h)) d[h] = Cls(h); return true; }, 0);
        return d;
    }

    static IntPtr FindOverflow()
    {
        IntPtr found = 0;
        EnumWindows((h, _) =>
        {
            if (Cls(h) == "TopLevelWindowForOverflowXamlIsland" && IsWindowVisible(h) && !Cloaked(h)) { found = h; return false; }
            return true;
        }, 0);
        return found;
    }

    static void Key(ushort vk, bool up, bool ext = false)
    {
        var i = new INPUT { type = 1, ki = new KI { vk = vk, flags = (up ? 2u : 0u) | (ext ? 1u : 0u) } };
        SendInput(1, new[] { i }, Marshal.SizeOf<INPUT>());
    }
    static void Tap(ushort vk) { Key(vk, false); Key(vk, true); }

    static AutomationElement? FindChevron(IntPtr tray)
    {
        var root = AutomationElement.FromHandle(tray);
        foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            string n, id;
            try { n = e.Current.Name; id = e.Current.AutomationId; } catch { continue; }
            if (n.Contains("hidden icons", StringComparison.OrdinalIgnoreCase) || id.Contains("Chevron", StringComparison.OrdinalIgnoreCase))
                return e;
        }
        return null;
    }

    static void Dump(IntPtr tray)
    {
        var root = AutomationElement.FromHandle(tray);
        foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            try
            {
                var c = e.Current;
                Say($"{c.ControlType.ProgrammaticName,-24} id='{c.AutomationId}' name='{c.Name}' class='{c.ClassName}' offscreen={c.IsOffscreen} rect={c.BoundingRectangle}");
            }
            catch { }
        }
    }

    static bool CloseFlyout()
    {
        for (int i = 0; i < 4 && FindOverflow() != 0; i++) { Tap(0x1B); Thread.Sleep(300); }
        var h = FindOverflow();
        if (h != 0) { Say("  (Esc did not close the flyout; hiding its window)"); ShowWindow(h, SW_HIDE); Thread.Sleep(300); }
        return FindOverflow() == 0;
    }

    static bool Invoke(AutomationElement chevron)
    {
        try
        {
            if (chevron.TryGetCurrentPattern(InvokePattern.Pattern, out var p))
            {
                var t0 = Stopwatch.StartNew();
                ((InvokePattern)p).Invoke();
                Say($"  Invoke() call took {t0.ElapsedMilliseconds} ms");
                return true;
            }
            Say("  chevron has no InvokePattern");
        }
        catch (Exception ex) { Say("  Invoke threw: " + ex.GetType().Name + " " + ex.Message); }
        return false;
    }

    // Returns ms since the call until the overflow window is visible, or -1 on timeout.
    static long WaitOverflow(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs) { if (FindOverflow() != 0) return sw.ElapsedMilliseconds; Thread.Sleep(5); }
        return -1;
    }

    static int Main(string[] args)
    {
        var tray = FindExplorerTray();
        if (tray == 0) { Say("no Shell_TrayWnd"); return 2; }
        bool initiallyVisible = IsWindowVisible(tray);
        GetWindowRect(tray, out var r0);
        Say($"initial: Shell_TrayWnd visible={initiallyVisible} rect=({r0.L},{r0.T},{r0.R},{r0.B}) fenn={Process.GetProcessesByName("fenn").Length > 0}");

        void Restore() { ShowWindow(tray, initiallyVisible ? SW_SHOWNA : SW_HIDE); }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
        Console.CancelKeyPress += (_, _) => Restore();

        if (args.Contains("--probe"))
        {
            try
            {
                var sw = Stopwatch.StartNew();
                ShowWindow(tray, SW_SHOWNA);
                bool last = !IsWindowVisible(tray); long lastCl = -1;
                while (sw.ElapsedMilliseconds < 1500)
                {
                    bool v = IsWindowVisible(tray); int cl = DwmGetWindowAttribute(tray, 14, out var cv, 4) == 0 ? cv : -1;
                    if (v != last || cl != lastCl) { GetWindowRect(tray, out var pr); Say($"  t={sw.ElapsedMilliseconds}ms visible={v} cloaked={cl} rect=({pr.L},{pr.T},{pr.R},{pr.B})"); last = v; lastCl = cl; }
                    Thread.Sleep(5);
                }
                Say("  probe done");
            }
            finally { Restore(); }
            return 0;
        }
        if (args.Contains("--dump"))
        {
            var dcts = new CancellationTokenSource(5000);
            try { ShowWindow(tray, SW_SHOWNA); _ = Task.Run(() => { while (!dcts.IsCancellationRequested) { PinShow(tray); Thread.Sleep(3); } }); Thread.Sleep(1200);
                GetWindowRect(tray, out var rs);
                Say($"after show: visible={IsWindowVisible(tray)} rect=({rs.L},{rs.T},{rs.R},{rs.B})");
                var root = AutomationElement.FromHandle(tray);
                Say($"root: name='{root.Current.Name}' class='{root.Current.ClassName}' children={root.FindAll(TreeScope.Children, Condition.TrueCondition).Count}");
                EnumChildWindows(tray, (h, _) => { GetWindowRect(h, out var cr); Say($"  child hwnd class={Cls(h)} visible={IsWindowVisible(h)} rect=({cr.L},{cr.T},{cr.R},{cr.B})"); return true; }, 0);
                foreach (var x in new[] { 1500, 1600, 1650, 1700, 1750, 1800, 1850 })
                {
                    try { var fe = AutomationElement.FromPoint(new System.Windows.Point(x, 1048)); Say($"  FromPoint({x},1048): {fe.Current.ControlType.ProgrammaticName} id='{fe.Current.AutomationId}' name='{fe.Current.Name}' class='{fe.Current.ClassName}' pid={fe.Current.ProcessId}"); } catch (Exception ex) { Say("  FromPoint err " + ex.Message); }
                }
                Dump(tray); }
            finally { dcts.Cancel(); Thread.Sleep(20); Restore(); }
            return 0;
        }
        if (!args.Contains("--run")) { Say("usage: --dump | --run"); return 1; }

        if (Process.GetProcessesByName("LogonUI").Length > 0) { Say("ABORT: session is locked (LogonUI running); keyboard input cannot reach the user desktop."); return 77; }
        if (!CloseFlyout()) { Say("flyout could not be closed, aborting"); return 3; }

        // ---- Strategy 1: taskbar hidden the whole time, UIA Invoke ----
        Say("== Strategy 1: SW_HIDE, UIA Invoke on chevron");
        try
        {
            ShowWindow(tray, SW_HIDE);
            var chev = FindChevron(tray);
            Say("  chevron found=" + (chev != null));
            if (chev != null && Invoke(chev))
            {
                long t = WaitOverflow(1500);
                Say(t >= 0 ? $"  RESULT overflow window appeared after {t} ms with taskbar hidden" : "  RESULT overflow window did NOT appear (1500 ms)");
            }
        }
        finally { Restore(); }
        if (!CloseFlyout()) { Say("flyout could not be closed, aborting"); return 3; }
        Thread.Sleep(500);

        int pinMs = args.Contains("--pin10") ? 10 : 3;
        CancellationTokenSource? pinCts = null;
        // ---- Strategy 2: show taskbar, invoke, hide ----
        Say("== Strategy 2: show taskbar, UIA Invoke, hide again");
        try
        {
            var before = VisibleTop();
            var sw = Stopwatch.StartNew();
            ShowWindow(tray, SW_SHOWNA);
            // Fenn re-hides the real taskbar ~30 ms after any show, so keep re-showing it (max 6 s) while we look for the chevron.
            pinCts = new CancellationTokenSource(6000);
            var pin = Task.Run(() => { while (!pinCts.IsCancellationRequested) { PinShow(tray); Thread.Sleep(pinMs); } });
            AutomationElement? chev = null;
            while (chev == null && sw.ElapsedMilliseconds < 1500) { chev = FindChevron(tray); if (chev == null) Thread.Sleep(10); }
            Say($"  chevron found={chev != null} after {sw.ElapsedMilliseconds} ms of taskbar visible");
            if (chev != null)
            {
                long tStart = sw.ElapsedMilliseconds;
                var inv = Task.Run(() => Invoke(chev));      // Invoke() blocks for a while, so poll for the flyout meanwhile
                if (args.Contains("--early-hide"))            // variant 2b: hide the taskbar 50 ms after starting Invoke, don't wait for the flyout
                {
                    Thread.Sleep(15);
                    pinCts?.Cancel(); ShowWindow(tray, SW_HIDE);
                    Say($"  early-hide: taskbar visible for only {sw.ElapsedMilliseconds} ms (show -> hide)");
                    tStart = sw.ElapsedMilliseconds;
                }
                long tOv = -1;
                while (sw.ElapsedMilliseconds - tStart < 4000 && tOv < 0) { if (FindOverflow() != 0) tOv = sw.ElapsedMilliseconds; else Thread.Sleep(5); }
                Say($"  Invoke started {tStart} ms after show; flyout window visible {tOv} ms after show ({(tOv >= 0 ? tOv - tStart : -1)} ms after Invoke started); Invoke returned={inv.IsCompleted}");
                foreach (var kv in VisibleTop().Where(k => !before.ContainsKey(k.Key))) Say($"  new visible top-level: {kv.Value} hwnd={kv.Key}");
                if (tOv >= 0)
                {
                    pinCts?.Cancel(); Thread.Sleep(20); if (!initiallyVisible) ShowWindow(tray, SW_HIDE);
                    long tHidden = sw.ElapsedMilliseconds;
                    Thread.Sleep(600);
                    bool stillOpen = FindOverflow() != 0;
                    Say($"  taskbar visible for {tHidden} ms total (show->hide); overflow still open 600 ms after hiding taskbar = {stillOpen}");
                    Say(stillOpen ? "  RESULT strategy 2 WORKS (flyout survives taskbar hide)" : "  RESULT overflow closed when taskbar hidden");
                }
                else Say("  RESULT overflow window did NOT appear");
            }
        }
        finally { pinCts?.Cancel(); Thread.Sleep(20); Restore(); }
        if (!CloseFlyout()) { Say("flyout could not be closed, aborting"); return 3; }
        Thread.Sleep(500);

        // ---- Strategy 3: Win+B, Enter with taskbar hidden ----
        Say("== Strategy 3: SW_HIDE, Win+B then Enter");
        try
        {
            ShowWindow(tray, SW_HIDE);
            Key(0x5B, false, true); Tap(0x42); Key(0x5B, true, true);
            Thread.Sleep(250);
            Tap(0x0D);
            long t = WaitOverflow(1500);
            Say(t >= 0 ? $"  RESULT overflow window appeared after {t} ms with taskbar hidden" : "  RESULT overflow window did NOT appear (1500 ms)");
        }
        finally { Restore(); }
        bool closed = CloseFlyout();
        Thread.Sleep(300);

        GetWindowRect(tray, out var r1);
        Say($"final: Shell_TrayWnd visible={IsWindowVisible(tray)} (initial {initiallyVisible}) rect=({r1.L},{r1.T},{r1.R},{r1.B}); overflow closed cleanly={closed}");
        return 0;
    }
}
