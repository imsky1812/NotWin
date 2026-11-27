using System.Globalization;

namespace NotWin.Core.Shell;

public enum SessionEndAction
{
    /// <summary>Nothing to do.</summary>
    None,

    /// <summary>Windows asked to end the session: give the taskbar back now (the process may be killed any moment).</summary>
    GiveBack,

    /// <summary>The shutdown or sign-out we gave the taskbar back for was cancelled: take the shell up again.</summary>
    Resume,
}

/// <summary>
/// The two session-end messages (user 2026-11-27: "nothing shell isn't working" after a Shut down that never happened).
/// WM_QUERYENDSESSION gives the taskbar back at once, as before. WM_ENDSESSION then says whether the session really
/// ends; when it does not (the user or an app cancelled), the shell resumes, once per query.
/// </summary>
public sealed class SessionEndTracker
{
    private bool _asked;

    public SessionEndAction Query()
    {
        _asked = true;
        return SessionEndAction.GiveBack;
    }

    public SessionEndAction Ended(bool ending)
    {
        if (!_asked) return SessionEndAction.None;
        _asked = false;
        return ending ? SessionEndAction.None : SessionEndAction.Resume;
    }
}

/// <summary><c>NotWin.exe --after &lt;pid&gt;</c>: a restarted shell waits for the old one to exit before it starts.</summary>
public static class RestartArgs
{
    public const string Flag = "--after";

    public static string[] For(int pid) => [Flag, pid.ToString(CultureInfo.InvariantCulture)];

    /// <summary>The process id to wait for, or null when the flag is absent or its value is not a positive number.</summary>
    public static int? WaitFor(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] != Flag) continue;
            return int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0 ? pid : null;
        }
        return null;
    }
}
