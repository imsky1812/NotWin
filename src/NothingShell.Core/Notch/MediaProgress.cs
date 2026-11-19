namespace NothingShell.Core.Notch;

/// <summary>Media tile helpers: the dotted progress bar, the m:ss clock and position interpolation between updates.</summary>
public static class MediaProgress
{
    /// <summary>Lit dots out of <paramref name="count"/> for the position; 0 when the duration is unknown.</summary>
    public static int Dots(TimeSpan position, TimeSpan duration, int count)
    {
        if (duration <= TimeSpan.Zero || count <= 0) return 0;
        return Math.Clamp((int)Math.Round(position / duration * count), 0, count);
    }

    /// <summary>"m:ss", or "h:mm:ss" from one hour; negative reads as 0:00.</summary>
    public static string Clock(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var s = (int)t.TotalSeconds;
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    /// <summary>
    /// The position now: sessions report a position and when it was taken; while playing it advances with the clock,
    /// capped at the duration (when known).
    /// </summary>
    public static TimeSpan Position(TimeSpan position, TimeSpan duration, DateTimeOffset at, bool playing, DateTimeOffset now)
    {
        var p = playing && at != default ? position + (now - at) : position;
        if (p < TimeSpan.Zero) p = TimeSpan.Zero;
        return duration > TimeSpan.Zero && p > duration ? duration : p;
    }
}
