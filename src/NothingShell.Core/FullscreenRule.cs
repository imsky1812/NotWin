namespace NothingShell.Core;

/// <param name="Foreground">DWM extended frame bounds of the foreground window.</param>
/// <param name="Monitor">Full bounds of the monitor the foreground window is on.</param>
/// <param name="IsMaximized">IsZoomed. With the taskbar auto-hidden, maximized windows also cover the monitor.</param>
/// <param name="QunsSaysFullscreen">SHQueryUserNotificationState is D3D full screen, busy or presentation mode.</param>
public readonly record struct FullscreenProbe(
    RectI Foreground, RectI Monitor, string ClassName, bool IsMaximized, bool QunsSaysFullscreen);

public static class FullscreenRule
{
    public static bool IsFullscreen(FullscreenProbe p)
    {
        if (p.QunsSaysFullscreen) return true;
        if (p.IsMaximized) return false;
        if (p.ClassName is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        return p.Foreground == p.Monitor;
    }

    /// <summary>
    /// The new state for a probe. A null probe (no foreground window for a moment, e.g. while a game switches
    /// windows) keeps the previous state instead of reporting "not full screen" and flashing the dock over it.
    /// </summary>
    public static bool Decide(FullscreenProbe? probe, bool previous) => probe is { } p ? IsFullscreen(p) : previous;
}
