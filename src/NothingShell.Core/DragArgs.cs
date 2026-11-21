using System.Text;

namespace NothingShell.Core;

/// <summary>
/// The command line of NothingShell.DragHelper (user, 2026-11-20: drag shelf files into WhatsApp, Google Forms /
/// Drive). The shell runs raised, and Windows refuses its drags into normal apps, so a small helper started at normal
/// level does the drag: <c>NothingShell.DragHelper.exe --drag "C:\path\file" …</c>.
/// </summary>
public static class DragArgs
{
    private const string Verb = "--drag";

    /// <summary>The paths after <c>--drag</c>, each fully qualified and well-formed; null for anything else.</summary>
    public static IReadOnlyList<string>? Parse(IReadOnlyList<string> args)
    {
        if (args.Count < 2 || args[0] != Verb) return null;
        var paths = args.Skip(1).ToList();
        foreach (var p in paths)
            if (string.IsNullOrEmpty(p) || p.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(p))
                return null;
        return paths;
    }

    /// <summary>
    /// The argument string for <see cref="Parse"/>: every path quoted; trailing backslashes doubled so a folder such as
    /// <c>C:\dir\</c> keeps its closing quote (the usual Windows command-line rules).
    /// </summary>
    public static string CommandLine(IReadOnlyList<string> paths)
    {
        var sb = new StringBuilder(Verb);
        foreach (var p in paths)
        {
            var trailing = p.Length - p.TrimEnd('\\').Length;
            sb.Append(" \"").Append(p).Append('\\', trailing).Append('"');
        }
        return sb.ToString();
    }
}
