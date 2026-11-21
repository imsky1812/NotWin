using System.Diagnostics;
using System.Runtime.InteropServices;
using NotWin.Shell;

namespace NotWin.Startup;

/// <summary>
/// <c>NotWin.exe --quit</c>: asks the running shell to quit through its normal exit path (taskbar restored,
/// appbar released, watchdog stopped) by broadcasting a registered window message. Exit code 0 when no other
/// NotWin is left within 10 s, 1 otherwise. Used by scripts (hands-on checks) instead of WM_CLOSE storms.
/// </summary>
public static class QuitCommand
{
    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    public static bool IsRequested(string[] args) => args.Any(a => string.Equals(a, "--quit", StringComparison.OrdinalIgnoreCase));

    public static int Run()
    {
        var msg = RegisterWindowMessage(TaskbarController.QuitMessageName);
        if (msg == 0) return 1;
        PostMessage(HwndBroadcast, msg, IntPtr.Zero, IntPtr.Zero);
        var self = Environment.ProcessId;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            var others = Process.GetProcessesByName("NotWin").Where(p => p.Id != self).ToList();
            foreach (var p in others) p.Dispose();
            if (others.Count == 0) return 0;
            Thread.Sleep(200);
        }
        return 1;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
