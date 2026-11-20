using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Dock;

/// <summary>
/// The real dot drain environment: the dock's monitor (the primary one) and DPI, the window's dock icon (or the spot
/// at the bottom centre where the hidden dock appears), the window's normal rectangle, the switch and what covers the
/// dock.
/// </summary>
/// <summary>Whether a dock item owns the window, and its icon's rectangle when the dock is showing (null while hidden).</summary>
public readonly record struct IconLookup(bool InDock, RectI? Rect);

public sealed class DockDrainEnvironment(Func<nint, IconLookup> lookup, Func<bool> enabled, Func<bool> suppressed) : IDrainEnvironment
{
    public bool Enabled => enabled();
    public bool Suppressed => suppressed();
    public RectI Monitor => DockWindowNative.GetPrimaryMonitor().Bounds;
    public double Scale => DockWindowNative.GetPrimaryMonitor().Dpi / 96.0;
    public double Pitch => 14 * Scale;

    public RectI? IconRect(nint hwnd)
    {
        var found = lookup(hwnd);
        if (!found.InDock) return null;
        var mon = Monitor;
        if (found.Rect is { } icon && icon.Top < mon.Bottom - 4 && icon.Bottom > mon.Top) return icon;
        // The dock is hidden below the screen: aim at the bottom centre, where it appears.
        var cx = (mon.Left + mon.Right) / 2;
        var y = mon.Bottom - (int)(30 * Scale);
        return new RectI(cx, y, cx, y);
    }

    public RectI? DrainRect(nint hwnd, bool restoring) => WindowListNative.DrainRect(hwnd, restoring);
}
