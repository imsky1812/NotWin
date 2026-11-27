using System.Windows.Interop;
using NotWin.Core;
using NotWin.Core.Shell;

namespace NotWin.Shell;

/// <summary>
/// Hears Windows' two session-end messages on a hidden top-level window that lives as long as the shell
/// (the taskbar watcher's window is disposed when the taskbar is given back, so it cannot hear the second one).
/// WM_QUERYENDSESSION: give the taskbar back now. WM_ENDSESSION(FALSE): the shutdown or sign-out was cancelled, so the
/// shell resumes (user 2026-11-27: after a cancelled Shut down NotWin stayed running with the Windows taskbar back).
/// </summary>
public sealed class SessionWatcher : IDisposable
{
    private const int WmQueryEndSession = 0x0011;
    private const int WmEndSession = 0x0016;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;

    private readonly SessionEndTracker _tracker = new();
    private HwndSource? _source;

    /// <summary>Windows asked to end the session.</summary>
    public event Action? GiveBack;

    /// <summary>The session end was cancelled after <see cref="GiveBack"/>.</summary>
    public event Action? Resume;

    /// <summary>The hidden window (tests send it the session messages).</summary>
    internal IntPtr Handle => _source?.Handle ?? IntPtr.Zero;

    public void Start()
    {
        _source = new HwndSource(new HwndSourceParameters("NotWinSessionWatcher")
        {
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow,
            Width = 0,
            Height = 0,
        });
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Never handled: DefWindowProc answers TRUE (the session may end), as before.
        if (msg == WmQueryEndSession)
        {
            if (_tracker.Query() == SessionEndAction.GiveBack)
            {
                Log.Info("session ending: giving the taskbar back");
                Raise(GiveBack);
            }
        }
        else if (msg == WmEndSession)
        {
            var ending = wParam != IntPtr.Zero;
            if (_tracker.Ended(ending) == SessionEndAction.Resume)
            {
                Log.Info("session end cancelled: resuming the shell");
                Raise(Resume);
            }
            else if (ending) Log.Info("session ends");
        }
        return IntPtr.Zero;
    }

    private static void Raise(Action? handler)
    {
        try { handler?.Invoke(); }
        catch (Exception e) { Log.Error("session watcher handler failed", e); }
    }

    public void Dispose()
    {
        try
        {
            _source?.RemoveHook(WndProc);
            _source?.Dispose();
        }
        catch (Exception) { }
        _source = null;
    }
}
