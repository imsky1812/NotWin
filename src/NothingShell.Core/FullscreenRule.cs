namespace NothingShell.Core;

/// <param name="Foreground">DWM extended frame bounds of the foreground window.</param>
/// <param name="Monitor">Full bounds of the monitor the foreground window is on.</param>
/// <param name="IsMaximized">IsZoomed. With the taskbar auto-hidden, maximized windows also cover the monitor.</param>
/// <param name="QunsSaysFullscreen">SHQueryUserNotificationState is D3D full screen, busy or presentation mode.</param>
/// <param name="HasFrame">The window has a title bar (WS_CAPTION) or a resize border (WS_THICKFRAME).</param>
public readonly record struct FullscreenProbe(
    RectI Foreground, RectI Monitor, string ClassName, bool IsMaximized, bool QunsSaysFullscreen, bool HasFrame = true);

public static class FullscreenRule
{
    public static bool IsFullscreen(FullscreenProbe p)
    {
        if (p.QunsSaysFullscreen) return true;
        if (p.ClassName is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        // A maximized window covers the monitor too when the taskbar is auto-hidden, so "maximized" alone is not
        // full screen. But a browser video going full screen from a maximized window stays maximized and drops its
        // title bar and resize border: frameless + covering the monitor is full screen, whatever QUNS says.
        if (p.IsMaximized) return !p.HasFrame && p.Foreground == p.Monitor;
        return p.Foreground == p.Monitor;
    }

    /// <summary>
    /// The new state for a probe. A null probe (no foreground window for a moment, e.g. while a game switches
    /// windows) keeps the previous state instead of reporting "not full screen" and flashing the dock over it.
    /// </summary>
    /// Our own windows in front (the launcher covers the whole monitor) keep the previous state too: they are not
    /// a game or a video, and treating them as one would close the launcher and hide the dock.
    /// While Windows is <paramref name="locked"/> the answer is always full screen (user report 2026-11-21: the shell's
    /// raised windows showed over the lock screen).
    public static bool Decide(FullscreenProbe? probe, bool previous, bool foregroundIsOwnProcess = false, bool locked = false)
        => locked || (foregroundIsOwnProcess ? previous : probe is { } p ? IsFullscreen(p) : previous);

    /// <summary>
    /// A maximized app is in front on <paramref name="dockMonitor"/> (user, 2026-11-21: the kept-visible dock then
    /// slides down). The desktop and windows on other monitors never count.
    /// </summary>
    public static bool IsMaximizedOn(FullscreenProbe? probe, RectI dockMonitor) =>
        probe is { IsMaximized: true } p && p.ClassName is not ("Progman" or "WorkerW" or "Shell_TrayWnd") && p.Monitor == dockMonitor;

    /// <summary>Like <see cref="Decide"/>: no foreground window, or our own window in front, keeps the previous state.</summary>
    public static bool DecideMaximized(FullscreenProbe? probe, RectI dockMonitor, bool previous, bool foregroundIsOwnProcess = false)
        => foregroundIsOwnProcess || probe is null ? previous : IsMaximizedOn(probe, dockMonitor);
}
