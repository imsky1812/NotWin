namespace NothingShell.Core.Dock;

/// <summary>
/// Shared helpers for the minimize animation (design 2026-11-20; user, 2026-11-21: option A, the window's live picture
/// shrinks into its dock icon — see <see cref="ShrinkMotion"/>).
/// </summary>
public static class DotDrain
{
    /// <summary>Where a minimizing window starts from: its work area when it was maximized, else its normal rectangle.</summary>
    public static RectI StartRect(RectI normal, bool maximized, RectI workArea) => maximized ? workArea : normal;

    /// <summary>The area an animation moves through: the window and its icon, plus <paramref name="pad"/> px all round.</summary>
    public static RectI Area(RectI window, RectI icon, int pad) => new(
        Math.Min(window.Left, icon.Left) - pad, Math.Min(window.Top, icon.Top) - pad,
        Math.Max(window.Right, icon.Right) + pad, Math.Max(window.Bottom, icon.Bottom) + pad);
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

/// <summary>At most four minimize animations at once (a minimize-all burst animates only the first four).</summary>
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
