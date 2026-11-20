namespace NothingShell.Core.Dock;

/// <summary>Where one horizontal strip of the window's picture is drawn (screen px).</summary>
public readonly record struct StripRect(double X, double Y, double Width, double Height);

/// <summary>
/// The genie as the window's own picture (user, 2026-11-21: "remove dots entirely, just keep the genie animation"):
/// the picture is cut into horizontal strips; each strip is placed along the genie funnel of <see cref="DotDrain.Genie"/>
/// — its top and bottom where those rows have fallen to, its width and centre those of its middle row. Enough strips
/// make the bend look continuous; a strip that has reached the icon is gone (null).
/// </summary>
public static class GenieStrips
{
    /// <summary>Strips for a window of <paramref name="heightPx"/>: about one per 6 px, between 24 and 120 (smooth edges).</summary>
    public static int CountFor(int heightPx) => Math.Clamp((int)Math.Round(heightPx / 6.0), 24, 120);

    public static IReadOnlyList<StripRect?> Layout(RectI window, double iconX, double iconY, double iconW, double p, int strips)
    {
        double w = window.Right - window.Left, h = window.Bottom - window.Top;
        var result = new StripRect?[strips];
        for (var i = 0; i < strips; i++)
        {
            double v0 = (double)i / strips, v1 = (double)(i + 1) / strips, vm = (v0 + v1) / 2;
            var top = Row(v0, 0);
            if (!top.Visible) continue;
            var bottom = Row(v1, 0);
            var bottomY = bottom.Visible ? bottom.Y : iconY;
            var midL = Row(vm, 0);
            var midR = Row(vm, 1);
            var (left, right) = midL.Visible && midR.Visible ? (midL.X, midR.X) : (top.X, Row(v0, 1).X);
            result[i] = new StripRect(left, top.Y, Math.Max(0, right - left), Math.Max(0, bottomY - top.Y));
        }
        return result;

        (double X, double Y, bool Visible) Row(double v, double u)
        {
            var g = DotDrain.Genie(new DrainDot(window.Left + u * w, window.Top + v * h, 0, 0), window, iconX, iconY, iconW, p);
            return (g.X, g.Y, g.Visible);
        }
    }
}
