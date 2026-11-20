using NothingShell.Core;
using NothingShell.Core.Dock;

namespace NothingShell.Dock;

/// <summary>What the dot drain needs from the desktop (the real one reads the dock, settings and screen).</summary>
public interface IDrainEnvironment
{
    /// <summary>The "Dot drain when minimizing" switch.</summary>
    bool Enabled { get; }

    /// <summary>A full-screen app is in front, or the launcher / panel covers the dock.</summary>
    bool Suppressed { get; }

    /// <summary>The dock's monitor (screen px).</summary>
    RectI Monitor { get; }

    /// <summary>Dot pitch in px (14 at 100 %, DPI-scaled).</summary>
    double Pitch { get; }

    /// <summary>The window's app icon in the dock (screen px), null when the window is not in the dock.</summary>
    RectI? IconRect(nint hwnd);

    /// <summary>The window's rectangle for the animation (screen px): its frame when restoring, where it was (maximized
    /// or normal) when minimizing; null when unknown.</summary>
    RectI? DrainRect(nint hwnd, bool restoring);
}

/// <summary>The shared overlay the dots are drawn on.</summary>
public interface IDrainOverlay
{
    /// <summary>The window's picture over <paramref name="screen"/> as grey cells of <paramref name="stepPx"/> (null = failed).</summary>
    void Snapshot(nint hwnd, RectI screen, int stepPx, Action<byte[]?> done);

    /// <summary>Plays the genie for <paramref name="dots"/> between <paramref name="window"/> and <paramref name="icon"/> (screen px).</summary>
    void Play(IReadOnlyList<DrainDot> dots, RectI window, RectI icon, bool restore, Action done);

    void Close();
}

/// <summary>
/// Minimize / restore → snapshot → dots → overlay (design 2026-11-20). Only windows in the dock, on the dock's monitor,
/// with the switch on and nothing covering the dock; at most four at once; grey outline dots when the picture is missing
/// or black; one shared overlay, closed when the last animation ends.
/// </summary>
public sealed class DotDrainController(IDrainEnvironment env, Func<IDrainOverlay> makeOverlay)
{
    private readonly DrainGate _gate = new();
    private IDrainOverlay? _overlay;

    public void OnMinimize(nint hwnd, bool start)
    {
        if (!env.Enabled || env.Suppressed) return;
        if (env.IconRect(hwnd) is not { } icon || env.DrainRect(hwnd, !start) is not { } normal) return;
        var monitor = env.Monitor;
        var cx = (normal.Left + normal.Right) / 2;
        var cy = (normal.Top + normal.Bottom) / 2;
        if (cx < monitor.Left || cx >= monitor.Right || cy < monitor.Top || cy >= monitor.Bottom) return;
        if (!_gate.TryStart()) return;

        var overlay = _overlay ??= makeOverlay();
        var step = DotDrain.StepFor(normal.Right - normal.Left, normal.Bottom - normal.Top, env.Pitch);
        double tx = (icon.Left + icon.Right) / 2.0, ty = (icon.Top + icon.Bottom) / 2.0;
        try
        {
            overlay.Snapshot(hwnd, normal, step, gray =>
            {
                // This runs later, from a timer: nothing may escape (an unhandled error ends the whole shell).
                try
                {
                    int cols = (normal.Right - normal.Left) / step, rows = (normal.Bottom - normal.Top) / step;
                    var dots = gray is null || gray.Length != cols * rows || DotDrain.IsNearBlack(gray)
                        ? DotDrain.Outline(normal.Left, normal.Top, normal.Right - normal.Left, normal.Bottom - normal.Top, step, tx, ty)
                        : DotDrain.FromImage(gray, cols, rows, normal.Left, normal.Top, step, tx, ty);
                    overlay.Play(dots, normal, icon, !start, () => Ended(overlay));
                }
                catch (Exception e)
                {
                    Log.Error("dot drain failed", e);
                    Ended(overlay);
                }
            });
        }
        catch (Exception e)
        {
            Log.Error("dot drain failed", e);
            Ended(overlay);
        }
    }

    private void Ended(IDrainOverlay overlay)
    {
        _gate.End();
        if (_gate.Running > 0 || !ReferenceEquals(overlay, _overlay)) return;
        _overlay = null;
        try { overlay.Close(); }
        catch (Exception e) { Log.Error("dot drain overlay close failed", e); }
    }
}
