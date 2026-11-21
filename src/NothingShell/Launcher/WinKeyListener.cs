using System.Windows.Threading;
using NothingShell.Core.Launcher;
using NothingShell.Native;

namespace NothingShell.Launcher;

/// <summary>
/// Turns a lone Win tap into <see cref="LauncherRequested"/> on the UI thread. The keyboard hook runs on the
/// shared <see cref="HookThread"/>; the callback only runs the detector and, for a lone tap, injects the mask
/// key so Start stays closed. Every other key and combo passes through untouched (Win+A/N too, until Plan 3).
/// </summary>
public sealed class WinKeyListener : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly LoneWinDetector _detector = new(KeyboardHookNative.IsPhysicallyDown);
    private readonly KeyboardHookNative.Hook _hook;
    private long _lastLogTicks;

    /// <summary>Raised on the UI thread after a lone Win tap.</summary>
    public event Action? LauncherRequested;

    public WinKeyListener(HookThread hookThread, Dispatcher ui)
    {
        _ui = ui;
        _hook = new KeyboardHookNative.Hook(hookThread, OnKey);
        Log.Info("win key hook installed");
    }

    // Hook thread. Tiny: detector + optional mask injection + a posted UI call.
    private bool OnKey(uint vk, bool down, bool injectedByUs)
    {
        var v = _detector.OnKey(vk, down, injectedByUs);
        if (v.InjectMaskFirst) KeyboardHookNative.InjectMaskKey();
        if (v.OpenLauncher) _ui.BeginInvoke(DispatcherPriority.Input, Raise);
        return v.Swallow;
    }

    private void Raise()
    {
        var now = Environment.TickCount64;
        if (now - _lastLogTicks > 5000) { _lastLogTicks = now; Log.Info("lone win -> launcher"); }
        try { LauncherRequested?.Invoke(); }
        catch (Exception e) { Log.Error("launcher request failed", e); }
    }

    public void Dispose() => _hook.Dispose();
}
