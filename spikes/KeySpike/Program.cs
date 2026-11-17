// THROWAWAY spike: lone Win key via WH_KEYBOARD_LL + mask key (VK 0xE8).
//   KeySpike.exe             install the hook and run until Ctrl+C (prints OPEN LAUNCHER / OPEN PANEL)
//   KeySpike.exe --selftest  drive the hook with SendInput and check whether Start opened
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

static class P
{
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
    const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_MASK = 0xE8, VK_A = 0x41, VK_N = 0x4E;
    const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2;
    static readonly IntPtr OurMark = 0x4E57; // dwExtraInfo on events we inject, so the hook ignores them

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct KBDLL { public uint vk, scan, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KI { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KI ki; [FieldOffset(32)] long pad; }
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr w, l; public uint time; public int x, y; }

    [DllImport("user32")] static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr mod, uint tid);
    [DllImport("user32")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("user32")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32")] static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
    [DllImport("user32")] static extern bool PostThreadMessage(uint tid, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32")] static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("user32")] static extern short GetAsyncKeyState(int vk);
    [DllImport("kernel32")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32")] static extern IntPtr GetModuleHandle(string? n);
    [DllImport("user32")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32")] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("dwmapi")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);

    // ---- hook state ----
    static bool winDown, comboUsed, panelCombo;
    static readonly HashSet<uint> swallowed = new();
    static readonly List<string> actions = new();
    static HookProc? proc; // keep alive

    static void Act(string s) { lock (actions) actions.Add(s); Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {s}"); }

    static void Send(ushort vk, bool up, bool ext = false, bool ours = true)
    {
        var i = new INPUT { type = 1, ki = new KI { vk = vk, flags = (up ? KEYEVENTF_KEYUP : 0) | (ext ? KEYEVENTF_EXTENDEDKEY : 0), extra = ours ? OurMark : 0 } };
        SendInput(1, new[] { i }, Marshal.SizeOf<INPUT>());
    }
    static void Mask() { Send(VK_MASK, false); Send(VK_MASK, true); }

    static IntPtr Hook(int code, IntPtr w, IntPtr l)
    {
        if (code < 0) return CallNextHookEx(0, code, w, l);
        var k = Marshal.PtrToStructure<KBDLL>(l);
        if (Environment.GetEnvironmentVariable("KS_TRACE") == "1") Console.WriteLine($"  hook vk=0x{k.vk:X2} flags=0x{k.flags:X} extra={k.extra} msg=0x{(int)w:X}");
        if (k.extra == OurMark) return CallNextHookEx(0, code, w, l);   // our own mask key

        int msg = (int)w;
        bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN, up = msg is WM_KEYUP or WM_SYSKEYUP;
        bool isWin = k.vk is VK_LWIN or VK_RWIN;

        if (isWin)
        {
            if (down) { if (!winDown) { winDown = true; comboUsed = false; panelCombo = false; } }   // pass through
            else if (up && winDown)
            {
                winDown = false;
                if (!comboUsed) { Mask(); Act("OPEN LAUNCHER"); }                  // mask BEFORE the keyup reaches the system
                else if (panelCombo) Mask();                                       // we ate the letter; stop Start on Win-up
                else Act($"pass-through: Win+combo released");
            }
            return CallNextHookEx(0, code, w, l);
        }

        if (winDown && down)
        {
            comboUsed = true;
            bool plain = (GetAsyncKeyState(0x10) & 0x8000) == 0 && (GetAsyncKeyState(0x11) & 0x8000) == 0 && (GetAsyncKeyState(0x12) & 0x8000) == 0;
            if (plain && k.vk is VK_A or VK_N)
            {
                swallowed.Add(k.vk);
                if (!panelCombo) { panelCombo = true; Act($"OPEN PANEL ({(k.vk == VK_A ? "Win+A" : "Win+N")})"); }
                return 1;                                                           // swallow the letter
            }
            Act($"pass-through: Win+0x{k.vk:X2}");
        }
        else if (up && swallowed.Remove(k.vk)) return 1;                            // swallow matching keyup
        else if (down && swallowed.Contains(k.vk)) return 1;                        // swallow autorepeat
        return CallNextHookEx(0, code, w, l);
    }

    // ---- Start menu detection ----
    static string Cls(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
    static string ProcName(IntPtr h) { GetWindowThreadProcessId(h, out var pid); try { return Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; } }
    static bool StartVisible(out string info)
    {
        string found = "";
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || (DwmGetWindowAttribute(h, 14, out var c, 4) == 0 && c != 0)) return true;
            var pn = ProcName(h);
            if (pn is "StartMenuExperienceHost") { found = $"{pn}:{Cls(h)} hwnd={h}"; return false; }
            return true;
        }, 0);
        var fg = GetForegroundWindow();
        string fgp = ProcName(fg);
        info = found != "" ? found : $"foreground={fgp}:{Cls(fg)}";
        return found != "" || fgp is "StartMenuExperienceHost" or "SearchHost";
    }

    static void Chord(params (ushort vk, bool ext)[] keys)
    {
        foreach (var (vk, ext) in keys) { Send(vk, false, ext, false); Thread.Sleep(20); }
        foreach (var (vk, ext) in keys.Reverse()) { Send(vk, true, ext, false); Thread.Sleep(20); }
    }
    static void CloseStartIfOpen(string why)
    {
        if (StartVisible(out var info)) { Console.WriteLine($"    (Start was open [{info}]; closing with Esc) {why}"); Send(0x1B, false, false, false); Send(0x1B, true, false, false); Thread.Sleep(700); }
    }

    static readonly List<(string name, bool pass, string detail)> results = new();
    static void Check(string name, bool pass, string detail) { results.Add((name, pass, detail)); Console.WriteLine($"  => {(pass ? "PASS" : "FAIL")} {name}: {detail}"); }
    static List<string> Snap() { lock (actions) { var c = actions.ToList(); actions.Clear(); return c; } }

    static HashSet<IntPtr> ExplorerWindows()
    {
        var s = new HashSet<IntPtr>();
        EnumWindows((h, _) => { if (Cls(h) == "CabinetWClass") s.Add(h); return true; }, 0);
        return s;
    }

    static void SelfTest()
    {
        if (Process.GetProcessesByName("LogonUI").Length > 0) { Console.WriteLine("ABORT: session is locked (LogonUI running); SendInput cannot reach the user desktop."); Environment.Exit(77); }
        var lwin = ((ushort)VK_LWIN, true);
        Console.WriteLine("-- control (hook NOT yet installed): lone Win tap must open Start, proving the detector works");
        Chord(lwin); Thread.Sleep(900);
        bool opened = StartVisible(out var ci);
        Console.WriteLine($"  control: Start visible={opened} ({ci})");
        CloseStartIfOpen("control");
        if (!opened) Console.WriteLine("  WARNING: control did not open Start; synthetic Win tap or detector is unreliable, (a) result is meaningless");

        // install the hook on the message-pump thread; this thread drives input
        uint pumpTid = 0; var ready = new ManualResetEventSlim();
        var pump = new Thread(() => { pumpTid = GetCurrentThreadId(); Run(ready); }) { IsBackground = true };
        pump.Start(); ready.Wait(); Thread.Sleep(300);
        Snap();

        Console.WriteLine("-- (a) lone Win tap with hook");
        Chord(lwin); Thread.Sleep(900);
        bool startAfter = StartVisible(out var si); var a = Snap();
        Console.WriteLine($"  actions: {string.Join(" | ", a)}");
        Check("(a) lone Win tap does not open Start", !startAfter && a.Contains("OPEN LAUNCHER"), $"startVisible={startAfter} ({si}); launcher event={a.Contains("OPEN LAUNCHER")}");
        CloseStartIfOpen("a");

        foreach (var (name, vk) in new[] { ("Win+A", (ushort)VK_A), ("Win+N", (ushort)VK_N) })
        {
            Console.WriteLine($"-- {name} with hook (letter must be swallowed, Start must not open)");
            Chord(lwin, (vk, false)); Thread.Sleep(900);
            bool s2 = StartVisible(out var i2); var l = Snap();
            Console.WriteLine($"  actions: {string.Join(" | ", l)}");
            Check($"{name} -> OPEN PANEL, Start stays closed", l.Any(x => x.StartsWith("OPEN PANEL")) && !s2 && !l.Contains("OPEN LAUNCHER"), $"startVisible={s2} ({i2})");
            CloseStartIfOpen(name);
        }

        Console.WriteLine("-- Win+E with hook (Explorer must open; we close the window we opened)");
        var before = ExplorerWindows();
        Chord(lwin, (0x45, false)); Thread.Sleep(1800);
        var newWins = ExplorerWindows().Where(h => !before.Contains(h)).ToList(); var e = Snap();
        Console.WriteLine($"  actions: {string.Join(" | ", e)}");
        Check("Win+E passes through (new Explorer window)", newWins.Count > 0 && !e.Contains("OPEN LAUNCHER") && e.Any(x => x.Contains("Win+0x45")), $"new CabinetWClass windows={newWins.Count}");
        foreach (var h in newWins) PostMessage(h, 0x10, 0, 0);   // WM_CLOSE
        Thread.Sleep(600);
        CloseStartIfOpen("e");

        Console.WriteLine("-- Win+D with hook (desktop shown, then Win+D again to undo)");
        Chord(lwin, (0x44, false)); Thread.Sleep(1000);
        var fg1 = GetForegroundWindow(); string fgDesc = $"{ProcName(fg1)}:{Cls(fg1)}"; var d = Snap();
        Console.WriteLine($"  actions: {string.Join(" | ", d)}; foreground after Win+D = {fgDesc}");
        Chord(lwin, (0x44, false)); Thread.Sleep(1000); Snap();
        Check("Win+D passes through", d.Any(x => x.Contains("Win+0x44")) && !d.Contains("OPEN LAUNCHER"), $"foreground after = {fgDesc} (Progman/WorkerW/Shell_TrayWnd = desktop shown)");
        CloseStartIfOpen("d");

        PostThreadMessage(pumpTid, 0x12, 0, 0); // WM_QUIT
        pump.Join(2000);
        Console.WriteLine("\n==== SUMMARY ====");
        foreach (var r in results) Console.WriteLine($"{(r.pass ? "PASS" : "FAIL")}  {r.name}  [{r.detail}]");
    }

    static void Run(ManualResetEventSlim? ready)
    {
        proc = Hook;
        var h = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
        if (h == 0) { Console.WriteLine("SetWindowsHookEx failed: " + Marshal.GetLastWin32Error()); ready?.Set(); return; }
        Console.WriteLine("hook installed");
        ready?.Set();
        while (GetMessage(out _, 0, 0, 0) > 0) { }
        UnhookWindowsHookEx(h);
        Console.WriteLine("hook removed");
    }

    static int Main(string[] args)
    {
        if (args.Contains("--selftest")) { SelfTest(); return results.All(r => r.pass) ? 0 : 1; }
        var tid = GetCurrentThreadId();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; PostThreadMessage(tid, 0x12, 0, 0); };
        Console.WriteLine("KeySpike running. Press Win alone / Win+A / Win+N. Ctrl+C to quit.");
        Run(null);
        return 0;
    }
}
