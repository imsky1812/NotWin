using System.Globalization;

namespace NotWin.Core.Tools;

/// <summary>Helpers for the notch's region tools (Grab text, Color, Snip).</summary>
public static class ToolMath
{
    private const int MinSize = 4;

    /// <summary>The rectangle dragged from <paramref name="a"/> to <paramref name="b"/> (any direction), clamped to
    /// <paramref name="bounds"/>; null when smaller than 4×4 px (a click, not a drag).</summary>
    public static RectI? Normalize(PointI a, PointI b, RectI bounds)
    {
        var l = Math.Clamp(Math.Min(a.X, b.X), bounds.Left, bounds.Right);
        var r = Math.Clamp(Math.Max(a.X, b.X), bounds.Left, bounds.Right);
        var t = Math.Clamp(Math.Min(a.Y, b.Y), bounds.Top, bounds.Bottom);
        var btm = Math.Clamp(Math.Max(a.Y, b.Y), bounds.Top, bounds.Bottom);
        return r - l < MinSize || btm - t < MinSize ? null : new RectI(l, t, r, btm);
    }

    public static string Hex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    public static string SnipFileName(DateTime local) =>
        "NotWin " + local.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture) + ".png";

    public static string JoinOcrLines(IEnumerable<string> lines) =>
        string.Join("\n", lines.Select(l => l.Trim()).Where(l => l.Length > 0));

    public static int WordCount(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
