namespace NothingShell.Core.Dock;

/// <summary>Where the window's live picture is drawn at one moment (screen px) and how opaque (0–1).</summary>
public readonly record struct ShrinkFrame(double X, double Y, double W, double H, double Opacity);

/// <summary>
/// The minimize animation (user, 2026-11-21, option A after the dot drain and the genie): the window's live picture
/// shrinks into its dock icon, keeping its shape, on a gentle curve (across first, then down) with a soft start and
/// landing, fading out over the last 30 %. Windows draws the picture (a DWM thumbnail), so only these numbers change
/// per frame.
/// </summary>
public static class ShrinkMotion
{
    public const double MinimizeMs = 380;

    public static ShrinkFrame At(RectI window, RectI icon, double p)
    {
        p = Math.Clamp(p, 0, 1);
        var e = Smoother(p);
        double w0 = Math.Max(1, window.Right - window.Left), h0 = Math.Max(1, window.Bottom - window.Top);
        var w1 = Math.Max(8, icon.Right - icon.Left);
        var h1 = w1 * h0 / w0;
        double cx0 = window.Left + w0 / 2, cy0 = window.Top + h0 / 2;
        double cx1 = (icon.Left + icon.Right) / 2.0, cy1 = (icon.Top + icon.Bottom) / 2.0;
        var w = w0 + (w1 - w0) * e;
        var h = h0 + (h1 - h0) * e;
        var cx = cx0 + (cx1 - cx0) * e;
        var cy = cy0 + (cy1 - cy0) * Math.Pow(e, 1.4);  // drops a little later than it slides across: a soft curve
        var opacity = p <= 0.7 ? 1 : 1 - Smoother((p - 0.7) / 0.3);
        return new ShrinkFrame(cx - w / 2, cy - h / 2, w, h, opacity);
    }

    private static double Smoother(double t) => t * t * t * (t * (t * 6 - 15) + 10);
}
