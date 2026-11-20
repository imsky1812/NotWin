using NothingShell.Core;
using NothingShell.Core.Dock;

namespace NothingShell.Dock;

/// <summary>What the minimize genie needs from the desktop (the real one reads the dock, settings and screen).</summary>
public interface IDrainEnvironment
{
    /// <summary>The "Genie when minimizing" switch.</summary>
    bool Enabled { get; }

    /// <summary>A full-screen app is in front, or the launcher / panel covers the dock.</summary>
    bool Suppressed { get; }

    /// <summary>The dock's monitor (screen px).</summary>
    RectI Monitor { get; }

    /// <summary>The window's app icon in the dock (screen px), null when the window is not in the dock.</summary>
    RectI? IconRect(nint hwnd);

    /// <summary>The window's rectangle for the animation (screen px): its frame when restoring, where it was (maximized
    /// or normal) when minimizing; null when unknown.</summary>
    RectI? DrainRect(nint hwnd, bool restoring);
}

/// <summary>The shared overlay the genie is drawn on.</summary>
public interface IDrainOverlay
{
    /// <summary>The window's picture over <paramref name="screen"/> (an image), or null when it could not be taken.</summary>
    void Snapshot(nint hwnd, RectI screen, Action<object?> done);

    /// <summary>Plays the genie of <paramref name="picture"/> (null = a plain black card) between the window and the icon.</summary>
    void Play(object? picture, RectI window, RectI icon, bool restore, Action done);

    void Close();
}

/// <summary>
/// Minimize / restore → snapshot → genie (user, 2026-11-21: the window's own picture, no dots). Only windows in the
/// dock, on the dock's monitor, with the switch on and nothing covering the dock; at most four at once; a black card
/// when the picture is missing; one shared overlay, closed when the last animation ends.
/// </summary>
public sealed class DotDrainController(IDrainEnvironment env, Func<IDrainOverlay> makeOverlay)
{
    private readonly DrainGate _gate = new();
    private IDrainOverlay? _overlay;

    public void OnMinimize(nint hwnd, bool start)
    {
        if (!env.Enabled || env.Suppressed) return;
        if (env.IconRect(hwnd) is not { } icon || env.DrainRect(hwnd, !start) is not { } window) return;
        var monitor = env.Monitor;
        var cx = (window.Left + window.Right) / 2;
        var cy = (window.Top + window.Bottom) / 2;
        if (cx < monitor.Left || cx >= monitor.Right || cy < monitor.Top || cy >= monitor.Bottom) return;
        if (!_gate.TryStart()) return;

        var overlay = _overlay ??= makeOverlay();
        try
        {
            overlay.Snapshot(hwnd, window, picture =>
            {
                // This can run later, from the dispatcher: nothing may escape (an unhandled error ends the shell).
                try { overlay.Play(picture, window, icon, !start, () => Ended(overlay)); }
                catch (Exception e)
                {
                    Log.Error("minimize genie failed", e);
                    Ended(overlay);
                }
            });
        }
        catch (Exception e)
        {
            Log.Error("minimize genie failed", e);
            Ended(overlay);
        }
    }

    private void Ended(IDrainOverlay overlay)
    {
        _gate.End();
        if (_gate.Running > 0 || !ReferenceEquals(overlay, _overlay)) return;
        _overlay = null;
        try { overlay.Close(); }
        catch (Exception e) { Log.Error("minimize genie overlay close failed", e); }
    }
}
