namespace NothingShell.Core.Dock;

/// <summary>One dot of a dot drain: its grid spot and radius (px) and when it starts moving (ms).</summary>
public readonly record struct DrainDot(double X, double Y, double R, double Delay);

/// <summary>
/// Dot drain (design 2026-11-20): a minimizing window becomes a halftone dot grid made from its own picture; the dots
/// travel along a curve pulled down toward the dock icon, nearest first, shrinking to 30 %. Restore runs it backwards.
/// Pure maths; the overlay draws what <see cref="At"/> returns.
/// </summary>
public static class DotDrain
{
    public const double TotalMs = 480, RestoreMs = 420, MaxStaggerMs = 180, ArrivalScale = 0.3, BlinkMs = 160;

    /// <summary>At most this many cells per window (four at once stay smooth; review 2026-11-21).</summary>
    public const int MaxCells = 4000;

    /// <summary>The cell size for a window: the pitch, grown until the window has at most <see cref="MaxCells"/> cells.</summary>
    public static int StepFor(int width, int height, double pitch)
    {
        var step = Math.Max(4, (int)Math.Round(pitch));
        while ((width / step) * (height / step) > MaxCells) step++;
        return step;
    }

    /// <summary>Where a minimizing window drains from: its work area when it was maximized, else its normal rectangle.</summary>
    public static RectI StartRect(RectI normal, bool maximized, RectI workArea) => maximized ? workArea : normal;
    /// <summary>Dot radius as a share of the pitch at full brightness, and for the grey outline (heavier dots, 2026-11-21).</summary>
    public const double RadiusFactor = 0.5, OutlineFactor = 0.38;
    private const double MinRadius = 0.8;

    /// <summary>True when at least 97 % of the samples are near-black (luminance under 12): the picture was not captured.</summary>
    public static bool IsNearBlack(byte[] gray)
    {
        if (gray.Length == 0) return true;
        var dark = gray.Count(g => g < 12);
        return dark >= gray.Length * 0.97;
    }

    /// <summary>
    /// Dots from a picture given as one grey byte per grid cell (<paramref name="cols"/> × <paramref name="rows"/>,
    /// row-major), cells <paramref name="pitch"/> px apart from (<paramref name="originX"/>, <paramref name="originY"/>).
    /// Brighter = bigger; cells too dark for a visible dot are left out.
    /// </summary>
    public static IReadOnlyList<DrainDot> FromImage(byte[] gray, int cols, int rows, double originX, double originY, double pitch,
        double targetX, double targetY)
    {
        var raw = new List<(double X, double Y, double R)>();
        for (var y = 0; y < rows; y++)
            for (var x = 0; x < cols; x++)
            {
                var i = y * cols + x;
                if (i >= gray.Length) break;
                var r = pitch * RadiusFactor * gray[i] / 255.0;
                if (r < MinRadius) continue;
                raw.Add((originX + x * pitch + pitch / 2, originY + y * pitch + pitch / 2, r));
            }
        return Staggered(raw, targetX, targetY);
    }

    /// <summary>Fallback B: grey dots filling the window's outline, one per cell.</summary>
    public static IReadOnlyList<DrainDot> Outline(double x, double y, double w, double h, double pitch, double targetX, double targetY)
    {
        var raw = new List<(double X, double Y, double R)>();
        for (var cy = y + pitch / 2; cy < y + h; cy += pitch)
            for (var cx = x + pitch / 2; cx < x + w; cx += pitch)
                raw.Add((cx, cy, pitch * OutlineFactor));
        return Staggered(raw, targetX, targetY);
    }

    // Nearest to the icon starts at 0 ms, the farthest at MaxStaggerMs.
    private static IReadOnlyList<DrainDot> Staggered(List<(double X, double Y, double R)> raw, double tx, double ty)
    {
        if (raw.Count == 0) return [];
        double Dist((double X, double Y, double R) d) => Math.Sqrt(Math.Pow(d.X - tx, 2) + Math.Pow(d.Y - ty, 2));
        var min = raw.Min(Dist);
        var max = raw.Max(Dist);
        var span = Math.Max(1e-9, max - min);
        return raw.Select(d => new DrainDot(d.X, d.Y, d.R, (Dist(d) - min) / span * MaxStaggerMs)).ToList();
    }

    /// <summary>Genie timing (user, 2026-11-21): minimize and restore, and the share of it spent forming the funnel.</summary>
    public const double GenieMs = 560, GenieRestoreMs = 500, BendShare = 0.35;

    /// <summary>
    /// The genie (user, 2026-11-21: "real physics of genie"): at progress <paramref name="p"/> (0 = the window, 1 = in the
    /// icon) the window's shape bends into a funnel whose narrow end is the icon's width — the bottom, nearest the icon,
    /// bends first — then the content slides down the funnel with a gravity-like acceleration; a dot that reaches the
    /// icon is absorbed (not visible). Restore runs p from 1 back to 0.
    /// </summary>
    public static (double X, double Y, double Scale, bool Visible) Genie(DrainDot d, RectI window, double iconX, double iconY, double iconW, double p)
    {
        p = Math.Clamp(p, 0, 1);
        double w = window.Right - window.Left, h = window.Bottom - window.Top;
        var u = w > 0 ? (d.X - window.Left) / w : 0.5;
        var bend = Smooth(Math.Clamp(p / BendShare, 0, 1));               // the funnel forms
        var fall = Math.Clamp((p - BendShare) / (1 - BendShare), 0, 1);
        fall *= fall;                                                       // then it accelerates in, like a falling body
        var y = d.Y + fall * (iconY - window.Top);                          // the top row ends exactly at the icon
        if (y >= iconY) return (iconX, iconY, ArrivalScale, false);
        var depth = Math.Clamp((y - window.Top) / Math.Max(1, iconY - window.Top), 0, 1);
        var pinch = bend * Smooth(depth);                                   // 0 = full width, 1 = the icon's width
        var centre = Lerp(window.Left + w / 2, iconX, pinch);
        var half = Lerp(w / 2, iconW / 2, pinch);
        return (centre + (u - 0.5) * 2 * half, y, Lerp(1, ArrivalScale, pinch), true);
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>
    /// Where a dot is at <paramref name="tMs"/>: minimizing, it waits for its delay, then moves along a quadratic curve
    /// (control point straight below/above it at the icon's height) with ease-in, shrinking to 30 %, arriving by
    /// <see cref="TotalMs"/>. Restoring runs the same path backwards over <see cref="RestoreMs"/>, farthest dots last.
    /// </summary>
    public static (double X, double Y, double Scale, double Alpha) At(DrainDot d, double targetX, double targetY, double tMs, bool restore)
    {
        double local;
        if (!restore)
            local = Math.Clamp((tMs - d.Delay) / (TotalMs - MaxStaggerMs), 0, 1);
        else
            local = 1 - Math.Clamp((tMs - (MaxStaggerMs - d.Delay)) / (RestoreMs - MaxStaggerMs), 0, 1);
        var p = local * local;
        var q = 1 - p;
        var x = q * q * d.X + 2 * q * p * d.X + p * p * targetX;
        var y = q * q * d.Y + 2 * q * p * targetY + p * p * targetY;
        return (x, y, 1 - (1 - ArrivalScale) * local, 1);
    }
}

/// <summary>At most four dot drains at once (a minimize-all burst animates only the first four).</summary>
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
