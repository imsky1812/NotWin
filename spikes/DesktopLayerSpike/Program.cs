// THROWAWAY spike: a clickable 300x300 WPF widget window on the desktop layer.
//   DesktopLayerSpike --option A|B [--selftest] [--stay] [--no-reshow]
//   A: send 0x052C to Progman, SetParent to the window that hosts SHELLDLL_DefView, z-order HWND_TOP among its children.
//   B: top-level WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE kept at HWND_BOTTOM (WM_WINDOWPOSCHANGING), re-shown on EVENT_SYSTEM_FOREGROUND -> desktop.
//   --selftest : safe automated checks (Win+D via SendInput, one verified click, UIA invoke, z-order test window), then exits.
//   --stay     : stay running with the re-attach watcher for the manual Explorer-restart check.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

static class P
{
    // ---- native ----
    delegate bool EnumProc(IntPtr h, IntPtr l);
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObj, int idChild, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public IntPtr hwnd, after; public int x, y, cx, cy; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] struct KI { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MI { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KI ki; [FieldOffset(8)] public MI mi; }
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string c, string? t);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string? c, string? t);
    [DllImport("user32")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32")] static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32")] static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32")] static extern IntPtr GetTopWindow(IntPtr h);
    [DllImport("user32")] static extern IntPtr SetParent(IntPtr c, IntPtr p);
    [DllImport("user32")] static extern IntPtr WindowFromPoint(PT p);
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern bool ScreenToClient(IntPtr h, ref PT p);
    [DllImport("user32")] static extern bool GetCursorPos(out PT p);
    [DllImport("user32")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32")] static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWL(IntPtr h, int i);
    [DllImport("user32", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWL(IntPtr h, int i, nint v);
    [DllImport("user32")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
    [DllImport("user32")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32")] static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint f, uint t, out IntPtr r);
    [DllImport("user32")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
    [DllImport("dwmapi")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);

    const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    const long WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_CAPTION = 0xC00000, WS_THICKFRAME = 0x40000, WS_SYSMENU = 0x80000, WS_VISIBLE = 0x10000000;
    const long WSX_TOOLWINDOW = 0x80, WSX_APPWINDOW = 0x40000, WSX_NOACTIVATE = 0x08000000;
    const uint SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
    static readonly IntPtr HWND_TOP = IntPtr.Zero, HWND_BOTTOM = (IntPtr)1;
    const int WM_WINDOWPOSCHANGING = 0x46;
    const int X = 100, Y = 300, SZ = 300;

    // ---- state ----
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static StreamWriter? logFile;
    static string Opt = "A";
    static bool NoReshow;
    static Application app = null!;
    static Window win = null!;
    static IntPtr hwnd, host, prog;
    static Button btn = null!;
    static int clicks, mouseUps;
    static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    static WinEventProc? keepAlive;

    static void Say(string s)
    {
        var line = $"[{Clock.ElapsedMilliseconds,6} ms] {s}";
        Console.WriteLine(line);
        try { logFile?.WriteLine(line); logFile?.Flush(); } catch { }
    }
    static string Cls(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    static string Title(IntPtr h) { var sb = new StringBuilder(128); GetWindowText(h, sb, 128); return sb.ToString(); }
    static string Owner(IntPtr h) { GetWindowThreadProcessId(h, out var pid); try { return $"{Process.GetProcessById((int)pid).ProcessName}({pid})"; } catch { return $"?({pid})"; } }
    static string Desc(IntPtr h) => h == 0 ? "null" : $"{h:X} class={Cls(h)} owner={Owner(h)} title='{Title(h)}'";
    static bool Cloaked(IntPtr h) => DwmGetWindowAttribute(h, 14, out var v, 4) == 0 && v != 0;
    static string Rc(IntPtr h) { GetWindowRect(h, out var r); return $"({r.L},{r.T},{r.R},{r.B}) {r.R - r.L}x{r.B - r.T}"; }
    static bool IsDesktopClass(IntPtr h) { var c = Cls(h); return c is "Progman" or "WorkerW"; }

    // ---- desktop tree ----
    static IntPtr FindDefViewHost(bool log)
    {
        prog = FindWindow("Progman", null);
        IntPtr found = 0;
        var kids = new List<IntPtr>();
        EnumChildWindows(prog, (h, _) => { kids.Add(h); return true; }, 0);
        if (log) { Say($"Progman {Desc(prog)} rect={Rc(prog)} visible={IsWindowVisible(prog)}"); foreach (var k in kids) Say($"  Progman descendant {Desc(k)} parent={GetParent(k):X} rect={Rc(k)} visible={IsWindowVisible(k)}"); }
        if (kids.Any(k => Cls(k) == "SHELLDLL_DefView" && GetParent(k) == prog)) found = prog;
        var wws = new List<IntPtr>();
        EnumWindows((h, _) => { if (Cls(h) == "WorkerW") wws.Add(h); return true; }, 0);
        foreach (var w in wws)
        {
            var dv = FindWindowEx(w, 0, "SHELLDLL_DefView", null);
            if (log) Say($"top-level WorkerW {w:X} owner={Owner(w)} visible={IsWindowVisible(w)} rect={Rc(w)} hasDefView={dv != 0}");
            if (dv != 0 && found == 0) found = w;
        }
        if (log) Say($"DefView host chosen: {(found == 0 ? "NONE" : Desc(found))}");
        return found;
    }

    // ---- window creation / attach ----
    static void Create()
    {
        btn = new Button { Content = "clicks: 0", Margin = new Thickness(40), FontSize = 20 };
        btn.Click += (_, _) => { clicks++; btn.Content = $"clicks: {clicks}"; };
        btn.PreviewMouseLeftButtonUp += (_, _) => mouseUps++;
        win = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Width = SZ, Height = SZ, Left = X, Top = Y, Title = "DesktopLayerSpike", Background = System.Windows.Media.Brushes.DimGray,
            Content = btn
        };
        hwnd = new WindowInteropHelper(win).EnsureHandle();
        HwndSource.FromHwnd(hwnd).AddHook(WndProc);
        win.Show();
        Attach();
        Say($"created window {hwnd:X} option={Opt}");
    }

    static bool Attach()
    {
        if (Opt == "A")
        {
            prog = FindWindow("Progman", null);
            SendMessageTimeout(prog, 0x052C, 0xD, 1, 0, 1000, out _);
            host = FindDefViewHost(true);
            if (host == 0) { Say("Option A: no DefView host found"); return false; }
            long st = GetWL(hwnd, GWL_STYLE);
            st = (st & ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU)) | WS_CHILD | WS_VISIBLE;
            SetWL(hwnd, GWL_STYLE, unchecked((nint)st));
            SetParent(hwnd, host);
            var p = new PT { X = X, Y = Y }; ScreenToClient(host, ref p);
            SetWindowPos(hwnd, HWND_TOP, p.X, p.Y, SZ, SZ, SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOACTIVATE);
            Say($"Option A: parented to {host:X}; client pos=({p.X},{p.Y}); rect now {Rc(hwnd)}");
            return GetParent(hwnd) == host;
        }
        else
        {
            SetWL(hwnd, GWL_EXSTYLE, GetWL(hwnd, GWL_EXSTYLE) | (nint)WSX_TOOLWINDOW | (nint)WSX_NOACTIVATE);
            SetWindowPos(hwnd, HWND_BOTTOM, X, Y, SZ, SZ, SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOACTIVATE);
            if (keepAlive == null)
            {
                keepAlive = (_, _, h, _, _, _, _) =>
                {
                    Say($"WinEvent FOREGROUND -> {Desc(h)}; spike visible={IsWindowVisible(hwnd)} iconic={IsIconic(hwnd)}");
                    if (!NoReshow && IsDesktopClass(h)) { SetWindowPos(hwnd, HWND_BOTTOM, X, Y, SZ, SZ, SWP_SHOWWINDOW | SWP_NOACTIVATE); Say($"  re-shown; visible={IsWindowVisible(hwnd)}"); }
                };
                SetWinEventHook(3, 3, 0, keepAlive, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT, delivered via this thread's message loop
            }
            Say($"Option B: toplevel bottom; rect {Rc(hwnd)} noReshow={NoReshow}");
            return true;
        }
    }

    static IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (Opt == "B" && msg == WM_WINDOWPOSCHANGING)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(l);
            wp.after = HWND_BOTTOM; wp.flags &= ~SWP_NOZORDER;
            Marshal.StructureToPtr(wp, l, false);
        }
        else if ((uint)msg == TaskbarCreated) { Say("TaskbarCreated received"); Watch(); }
        return IntPtr.Zero;
    }

    // ---- re-attach watcher (criterion d, manual) ----
    static long lostAt = -1, progSeenAt = -1;
    static void Watch()
    {
        bool dead = !IsWindow(hwnd);
        var curProg = FindWindow("Progman", null);
        bool bad = dead || (Opt == "A" && (curProg == 0 || curProg != prog || host == 0 || GetParent(hwnd) != host)) || (Opt == "B" && !IsWindowVisible(hwnd));
        if (!bad) { lostAt = progSeenAt = -1; return; }
        long now = Clock.ElapsedMilliseconds;
        if (lostAt < 0) { lostAt = now; Say($"WATCH: lost (dead={dead} progman={curProg:X} was {prog:X} host={host:X})"); }
        if (Opt == "A" && curProg == 0) return; // explorer not back yet
        if (progSeenAt < 0) { progSeenAt = now; Say($"WATCH: Progman {curProg:X} first seen {now - lostAt} ms after loss detected"); }
        bool ok;
        if (dead) { try { win.Close(); } catch { } Create(); ok = Opt != "A" || (host != 0 && GetParent(hwnd) == host); }
        else ok = Attach();
        if (!ok) { Say($"WATCH: attach incomplete (DefView host not ready?); will retry; {now - progSeenAt} ms since Progman first seen"); return; }
        long t = Clock.ElapsedMilliseconds;
        Say($"WATCH: re-attached; visible={IsWindowVisible(hwnd)} parent={GetParent(hwnd):X}; {t - progSeenAt} ms since new Progman first seen, {t - lostAt} ms since loss detected");
        lostAt = progSeenAt = -1;
    }

    // ---- inspection ----
    static (int idx, int total) ZIndex(IntPtr h)
    {
        var parent = GetParent(h); int i = 0, at = -1;
        for (var c = GetTopWindow(parent); c != 0; c = GetWindow(c, 2 /*GW_HWNDNEXT*/)) { if (c == h) at = i; i++; }
        return (at, i);
    }
    static PT Center() { GetWindowRect(hwnd, out var r); return new PT { X = (r.L + r.R) / 2, Y = (r.T + r.B) / 2 }; }
    static PT ButtonCenter() => (PT)win.Dispatcher.Invoke(() => { var p = btn.PointToScreen(new System.Windows.Point(btn.ActualWidth / 2, btn.ActualHeight / 2)); return new PT { X = (int)p.X, Y = (int)p.Y }; });

    static void Describe(string label)
    {
        Say($"--- state: {label}");
        var st = GetWL(hwnd, GWL_STYLE); var ex = GetWL(hwnd, GWL_EXSTYLE);
        var c = Center(); var at = WindowFromPoint(c);
        var (zi, zt) = ZIndex(hwnd);
        Say($"  hwnd={hwnd:X} IsWindow={IsWindow(hwnd)} IsWindowVisible={IsWindowVisible(hwnd)} IsIconic={IsIconic(hwnd)} cloaked={Cloaked(hwnd)} rect={Rc(hwnd)}");
        Say($"  style=0x{st:X} exstyle=0x{ex:X} (TOOLWINDOW={(ex & WSX_TOOLWINDOW) != 0} APPWINDOW={(ex & WSX_APPWINDOW) != 0} NOACTIVATE={(ex & WSX_NOACTIVATE) != 0} CHILD={(st & WS_CHILD) != 0}) parent={GetParent(hwnd):X} owner(GW_OWNER)={GetWindow(hwnd, 4):X}");
        Say($"  z-order among siblings of its parent: index {zi} of {zt} (0 = topmost)");
        var root = GetAncestor(hwnd, 2 /*GA_ROOT*/);
        if (root != hwnd) { var (ri, rt) = ZIndex(root); Say($"  top-level root {Desc(root)} z-index among top-level windows: {ri} of {rt}"); }
        { var pg = FindWindow("Progman", null); var (pi, pt) = ZIndex(pg); Say($"  Progman z-index among top-level windows: {pi} of {pt} (spike top-level index {(GetParent(hwnd) == 0 ? ZIndex(hwnd).idx.ToString() : "n/a, child")})"); }
        bool inEnum = false; EnumWindows((h, _) => { if (h == hwnd) inEnum = true; return true; }, 0);
        Say($"  present in EnumWindows (top-level enumeration): {inEnum}");
        bool altTab = IsWindowVisible(hwnd) && !Cloaked(hwnd) && GetWindow(hwnd, 4) == 0 && (st & WS_CHILD) == 0 && ((ex & WSX_TOOLWINDOW) == 0 || (ex & WSX_APPWINDOW) != 0);
        Say($"  Alt-Tab heuristic (visible && !cloaked && unowned && top-level && (!TOOLWINDOW || APPWINDOW)) = {altTab}  [computed; Alt-Tab not pressed]");
        Say($"  WindowFromPoint(center {c.X},{c.Y}) = {Desc(at)}  isSpike={at == hwnd}");
        Say($"  foreground = {Desc(GetForegroundWindow())}");
    }

    // ---- safe input ----
    static bool LogonUi() => Process.GetProcessesByName("LogonUI").Length > 0;
    static void Key(ushort vk, bool up) => SendInput(1, new[] { new INPUT { type = 1, ki = new KI { vk = vk, flags = up ? 2u : 0u } } }, 40);
    static void WinD() { Key(0x5B, false); Key(0x44, false); Thread.Sleep(30); Key(0x44, true); Key(0x5B, true); }

    static void ClickTest(string label)
    {
        Say($"--- click test: {label}");
        if (LogonUi()) { Say("  ABORT: LogonUI running"); return; }
        var c = ButtonCenter(); var at = WindowFromPoint(c);
        Say($"  button center {c.X},{c.Y}; WindowFromPoint = {Desc(at)}");
        if (at != hwnd) Say("  SKIP SendInput click: point is not over the spike window (safety rule)");
        else
        {
            int b = clicks, mu = mouseUps; GetCursorPos(out var old);
            SetCursorPos(c.X, c.Y); Thread.Sleep(150);
            if (WindowFromPoint(c) != hwnd) { Say("  SKIP: re-check failed after cursor move"); SetCursorPos(old.X, old.Y); }
            else
            {
                SendInput(2, new[] { new INPUT { type = 0, mi = new MI { flags = 2 } }, new INPUT { type = 0, mi = new MI { flags = 4 } } }, 40);
                Thread.Sleep(400); SetCursorPos(old.X, old.Y);
                Say($"  SendInput click: clicks {b} -> {clicks}, mouse-up events {mu} -> {mouseUps}  => {(clicks > b ? "REGISTERED" : "NOT registered")}");
            }
        }
        try
        {
            int b = clicks;
            var el = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            if (el == null) Say("  UIA: no Button element found");
            else { ((InvokePattern)el.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); Thread.Sleep(300); Say($"  UIA InvokePattern: clicks {b} -> {clicks} (secondary check; no mouse involved)"); }
        }
        catch (Exception e) { Say($"  UIA error: {e.GetType().Name}: {e.Message}"); }
    }

    static void ZTest()
    {
        Say("--- z-order test: own top-level test window over the spike");
        var c = Center(); IntPtr zh = 0; Window? z = null;
        win.Dispatcher.Invoke(() =>
        {
            z = new Window { Title = "ZTestWindow", Width = 400, Height = 400, Left = c.X - 200, Top = c.Y - 200, WindowStyle = WindowStyle.ToolWindow, Content = new TextBlock { Text = "z-order test window (spike)" } };
            z.Show(); zh = new WindowInteropHelper(z).Handle;
        });
        Thread.Sleep(600);
        var at = WindowFromPoint(c);
        Say($"  WindowFromPoint(spike center) = {Desc(at)}  isTestWindow={at == zh}");
        var root = GetAncestor(hwnd, 2); bool found = false; int steps = 0;
        for (var n = GetWindow(zh, 2); n != 0; n = GetWindow(n, 2)) { steps++; if (n == root) { found = true; break; } }
        Say($"  walking GW_HWNDNEXT from test window {zh:X}: spike's top-level root {root:X} ({(root == hwnd ? "the spike itself" : "parent-chain root, spike is a child")}) found below it = {found} after {steps} steps");
        var zs = ZIndex(zh);
        Say($"  test window top-level z-index {zs.idx} of {zs.total}");
        win.Dispatcher.Invoke(() => z!.Close());
        Thread.Sleep(400);
        Say($"  test window closed; IsWindow(test)={IsWindow(zh)}; foreground={Desc(GetForegroundWindow())}");
    }

    static void SelfTest()
    {
        bool toggled = false;
        try
        {
            Thread.Sleep(1500);
            Say($"=== SELFTEST option {Opt} noReshow={NoReshow} OS={Environment.OSVersion.Version}");
            if (LogonUi()) { Say("ABORT: LogonUI running (session locked?)"); return; }
            var fg0 = GetForegroundWindow();
            Say($"foreground before: {Desc(fg0)}");
            Describe("initial (normal desktop, user's windows present)");
            ZTest();
            Describe("after z-test");
            fg0 = GetForegroundWindow();
            Say($"foreground before Win+D: {Desc(fg0)}");
            if (IsDesktopClass(fg0)) { Say("desktop already foreground; skipping Win+D toggles"); ClickTest("no Win+D"); return; }
            Say(">>> Win+D #1");
            toggled = true; WinD(); Thread.Sleep(1200);
            Describe("after Win+D #1 (desktop shown)");
            ClickTest("desktop shown");
            Say(">>> Win+D #2 (restore)");
            WinD(); toggled = false; Thread.Sleep(1500);
            Describe("after Win+D #2 (restored)");
            var fg1 = GetForegroundWindow();
            Say($"foreground same as before Win+D: {fg1 == fg0}  (before {fg0:X}, after {fg1:X})");
            Say("--- re-attach code path (NOT an Explorer restart)");
            win.Dispatcher.Invoke(() => { if (Opt == "B") SendMessage(hwnd, TaskbarCreated, 0, 0); else Attach(); });
            Thread.Sleep(500);
            Describe("after simulated re-attach call");
        }
        catch (Exception e) { Say("SELFTEST EXCEPTION " + e); }
        finally
        {
            if (toggled) { Say(">>> FINALLY: restoring Win+D (exception path)"); WinD(); Thread.Sleep(1500); Say($"foreground after emergency restore: {Desc(GetForegroundWindow())}"); }
            Say("=== SELFTEST END");
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        Opt = args.SkipWhile(a => a != "--option").Skip(1).FirstOrDefault()?.ToUpperInvariant() ?? "A";
        NoReshow = args.Contains("--no-reshow");
        bool self = args.Contains("--selftest"), stay = args.Contains("--stay");
        if (stay) logFile = new StreamWriter(Path.Combine(Path.GetTempPath(), "DesktopLayerSpike.log"), true);
        app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Create();
        if (stay)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) }; t.Tick += (_, _) => Watch(); t.Start();
            Say($"staying; log also in {Path.Combine(Path.GetTempPath(), "DesktopLayerSpike.log")}. Ctrl+C to quit");
        }
        if (self) Task.Run(SelfTest).ContinueWith(_ => app.Dispatcher.Invoke(() => { try { win.Close(); } catch { } app.Shutdown(); }));
        app.Run();
        Say("exited");
    }
}
