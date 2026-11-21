using System.Globalization;
using System.Text;

namespace NotWin.Core;

/// <summary>
/// What the drag helper was asked to do: the paths, the notch window it waits on (0: start at once) and the press's
/// token, which the shell puts on that window when the press becomes a drag.
/// </summary>
public sealed record DragRequest(IReadOnlyList<string> Paths, nint Over, long Token = 0);

public enum DragGateStep { Wait, Go, Clicked, GiveUp }

/// <summary>
/// The helper starts when a shelf chip is pressed (Plan 8: process start-up no longer races a quick drag) and waits
/// until the shell puts this press's token on the notch window (the press became a drag) — never the window's style,
/// which the notch changes by itself whenever it closes (review, Plan 8). The button let go first means it was a click
/// (the shell copies the file). Holding still is fine; after ten minutes it gives up.
/// </summary>
public static class DragGate
{
    public const string Property = "NotWin.DragGo";
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);

    public static DragGateStep Next(bool buttonDown, bool goSignal, TimeSpan elapsed) =>
        !buttonDown ? DragGateStep.Clicked
        : goSignal ? DragGateStep.Go
        : elapsed >= Limit ? DragGateStep.GiveUp
        : DragGateStep.Wait;
}

/// <summary>
/// The command line of NotWin.DragHelper (user, 2026-11-20: drag shelf files into WhatsApp, Google Forms /
/// Drive). The shell runs raised, and Windows refuses its drags into normal apps, so a small helper started at normal
/// level does the drag: <c>NotWin.DragHelper.exe --drag "C:\path\file" …</c>.
/// </summary>
public static class DragArgs
{
    private const string Verb = "--drag", OverFlag = "--over", TokenFlag = "--token";

    /// <summary>The paths after <c>--drag</c>, each fully qualified and well-formed; null for anything else.</summary>
    public static IReadOnlyList<string>? Parse(IReadOnlyList<string> args) => ParseRequest(args)?.Paths;

    /// <summary>
    /// <c>[--over &lt;hex window&gt;] --drag "path" …</c>: the paths (as <see cref="Parse"/>) and the notch window to wait
    /// on; null for anything else.
    /// </summary>
    public static DragRequest? ParseRequest(IReadOnlyList<string> args)
    {
        nint over = 0;
        long token = 0;
        if (args.Count >= 2 && args[0] == OverFlag)
        {
            if (!long.TryParse(args[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var h) || h == 0) return null;
            over = (nint)h;
            // A waiting helper needs its press's token.
            if (args.Count < 4 || args[2] != TokenFlag
                || !long.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out token) || token == 0) return null;
            args = args.Skip(4).ToList();
        }
        if (args.Count < 2 || args[0] != Verb) return null;
        var paths = args.Skip(1).ToList();
        foreach (var p in paths)
            if (string.IsNullOrEmpty(p) || p.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(p))
                return null;
        return new DragRequest(paths, over, token);
    }

    /// <summary>
    /// The argument string for <see cref="Parse"/>: every path quoted; trailing backslashes doubled so a folder such as
    /// <c>C:\dir\</c> keeps its closing quote (the usual Windows command-line rules).
    /// </summary>
    public static string CommandLine(IReadOnlyList<string> paths, nint over = 0, long token = 0)
    {
        var sb = new StringBuilder();
        if (over != 0)
            sb.Append(OverFlag).Append(' ').Append(((long)over).ToString("x", CultureInfo.InvariantCulture))
              .Append(' ').Append(TokenFlag).Append(' ').Append(token.ToString(CultureInfo.InvariantCulture)).Append(' ');
        sb.Append(Verb);
        foreach (var p in paths)
        {
            var trailing = p.Length - p.TrimEnd('\\').Length;
            sb.Append(" \"").Append(p).Append('\\', trailing).Append('"');
        }
        return sb.ToString();
    }
}
