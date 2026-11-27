using System.Globalization;

namespace NotWin.Core.Shell;

/// <summary>
/// The installed copy started without uiAccess (user 2026-11-27: twice after a reboot the start-with-Windows launch came
/// up without it, so the dock could not stay on top and NotWin looked closed; started by hand it always had it). It then
/// relaunches itself through Explorer after a short wait, at most <see cref="MaxTries"/> times.
/// </summary>
public static class UiAccessRetry
{
    public const string Flag = "--uiaccess-try";
    public const int MaxTries = 2;

    public static bool ShouldRelaunch(bool uiAccess, bool installedCopy, int attempt) =>
        !uiAccess && installedCopy && attempt < MaxTries;

    /// <summary>Which try this start is (0 = a normal start).</summary>
    public static int Attempt(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (args[i] == Flag)
                return int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
        return 0;
    }

    /// <summary>The next copy's arguments: wait for this one, and count the try.</summary>
    public static string[] Next(int pid, int attempt) =>
        [.. RestartArgs.For(pid), Flag, (attempt + 1).ToString(CultureInfo.InvariantCulture)];

    /// <summary>5 s, then 10 s: long enough for whatever Windows was still doing after sign-in.</summary>
    public static TimeSpan Delay(int attempt) => TimeSpan.FromSeconds(5 * (attempt + 1));
}
