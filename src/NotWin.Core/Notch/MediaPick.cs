namespace NotWin.Core.Notch;

/// <summary>
/// Which media session the notch and the media tile / widget follow, out of every app that reports to Windows' media
/// controls (user report 2026-11-20: Windows' own "current" session stayed on Spotify while a YouTube video played).
/// A playing session beats a paused one; the one already shown is kept while it plays (no flipping between two);
/// among the others the one that started playing last wins. With nothing playing, the shown one stays, else Windows'
/// current, else the first.
/// </summary>
public static class MediaPick
{
    /// <param name="playingSince">When the session last started playing (any increasing clock), for tie-breaks.</param>
    public static T? Choose<T>(IReadOnlyList<T> sessions, Func<T, bool> playing, Func<T, long> playingSince, T? previous, T? system)
        where T : class
    {
        if (previous is not null && sessions.Contains(previous) && playing(previous)) return previous;
        var latest = sessions.Where(playing).OrderByDescending(playingSince).FirstOrDefault();
        if (latest is not null) return latest;
        if (previous is not null && sessions.Contains(previous)) return previous;
        if (system is not null && sessions.Contains(system)) return system;
        return sessions.Count > 0 ? sessions[0] : null;
    }
}
