using System.Runtime.InteropServices;
using NotWin.Shell;
using static NotWin.Tests.WidgetSurfaceTests;

namespace NotWin.Tests;

/// <summary>User 2026-11-27: after a cancelled Shut down NotWin stayed running with the Windows taskbar back.</summary>
public sealed class SessionWatcherTests
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WmQueryEndSession = 0x0011, WmEndSession = 0x0016;

    [Fact]
    public void CancelledShutdown_GivesBackThenResumes() => OnSta(() =>
    {
        using var w = new SessionWatcher();
        var log = new List<string>();
        w.GiveBack += () => log.Add("give back");
        w.Resume += () => log.Add("resume");
        w.Start();
        var allowed = SendMessage(w.Handle, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        SendMessage(w.Handle, WmEndSession, IntPtr.Zero, IntPtr.Zero); // FALSE: cancelled
        Assert.Equal(["give back", "resume"], log);
        Assert.NotEqual(IntPtr.Zero, allowed); // still lets Windows end the session
    });

    [Fact]
    public void RealShutdown_DoesNotResume() => OnSta(() =>
    {
        using var w = new SessionWatcher();
        var resumed = false;
        w.Resume += () => resumed = true;
        w.Start();
        SendMessage(w.Handle, WmQueryEndSession, IntPtr.Zero, IntPtr.Zero);
        SendMessage(w.Handle, WmEndSession, new IntPtr(1), IntPtr.Zero); // TRUE: the session ends
        Assert.False(resumed);
    });
}
