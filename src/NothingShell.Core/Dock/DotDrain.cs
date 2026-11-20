namespace NothingShell.Core.Dock;

/// <summary>A point of the window (screen px) that the genie moves; R and Delay are unused leftovers kept for callers.</summary>
public readonly record struct DrainDot(double X, double Y, double R = 0, double Delay = 0);

/// <summary>
/// The minimize genie's maths (design 2026-11-20; user, 2026-11-21: dots removed, the window's own picture does the
/// genie — see <see cref="GenieStrips"/>). Pure: the overlay draws what these return.
/// </summary>
public static class DotDrain
{
    /// <summary>Genie timing: minimize and restore, and the share of it spent forming the funnel.</summary>
    public const double GenieMs = 620, GenieRestoreMs = 560, BendShare = 0.4;

    /// <summary>Where a minimizing window starts from: its work area when it was maximized, else its normal rectangle.</summary>
    public static RectI StartRect(RectI normal, bool maximized, RectI workArea) => maximized ? workArea : normal;

    /// <summary>The area an animation moves through: the window and its icon, plus <paramref name="pad"/> px all round.</summary>
    public static RectI Area(RectI window, RectI icon, int pad) => new(
        Math.Min(window.Left, icon.Left) - pad, Math.Min(window.Top, icon.Top) - pad,
        Math.Max(window.Right, icon.Right) + pad, Math.Max(window.Bottom, icon.Bottom) + pad);

    /// <summary>
    /// The genie: at progress <paramref name="p"/> (0 = the window, 1 = in the icon) the window's shape bends into a
    /// funnel whose narrow end is the icon's width — the bottom, nearest the icon, bends first — then the content slides
    /// down the funnel; a point that reaches the icon is absorbed (not visible). Restore runs p from 1 back to 0.
    /// Soft start and landing (smootherstep), the slide overlapping the end of the bend, curved funnel sides.
    /// </summary>
    public static (double X, double Y, double Scale, bool Visible) Genie(DrainDot d, RectI window, double iconX, double iconY, double iconW, double p)
    {
        p = Math.Clamp(p, 0, 1);
        double w = window.Right - window.Left;
        var u = w > 0 ? (d.X - window.Left) / w : 0.5;
        var bend = Smoother(Math.Clamp(p / BendShare, 0, 1));             // the funnel forms, bottom first
        var fall = Smoother(Math.Clamp((p - 0.3) / 0.7, 0, 1));            // then the content slides in
        var y = d.Y + fall * (iconY - window.Top);                          // the top row ends exactly at the icon
        if (y >= iconY - 1) return (iconX, iconY, 0.35, false);
        var depth = Math.Clamp((y - window.Top) / Math.Max(1, iconY - window.Top), 0, 1);
        var pinch = bend * Math.Pow(Smooth(depth), 0.85);                   // 0 = full width, 1 = the icon's width
        var centre = Lerp(window.Left + w / 2, iconX, pinch);
        var half = Lerp(w / 2, iconW / 2, pinch);
        return (centre + (u - 0.5) * 2 * half, y, Lerp(1, 0.35, pinch), true);
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private static double Smoother(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

/// <summary>Frame timing of one animation, for the log (smoothness is measured, not guessed).</summary>
public sealed class FrameStats
{
    private double? _last;

    public int Frames { get; private set; }
    public double WorstGapMs { get; private set; }

    /// <summary>A frame was drawn at <paramref name="ms"/> since the animation started.</summary>
    public void At(double ms)
    {
        if (_last is { } last) WorstGapMs = Math.Max(WorstGapMs, ms - last);
        _last = ms;
        Frames++;
    }
}

/// <summary>At most four genies at once (a minimize-all burst animates only the first four).</summary>
public sealed class DrainGate
{
    public const int Max = 4;

    public int Running { get; private set; }

    public bool TryStart()
    {
        if (Running >= Max) return false;
        Running++;
        return true;
    }

    public void End() => Running = Math.Max(0, Running - 1);
}
