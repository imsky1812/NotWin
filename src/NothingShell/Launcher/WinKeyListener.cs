using System.Windows.Threading;
using NothingShell.Core.Launcher;
using NothingShell.Core.Widgets;
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

    /// <summary>Raised on the UI thread for Win+A / Win+N (Windows' own panels do not open).</summary>
    public event Action? PanelRequested;

    /// <summary>The snake's key gate (Plan 5): while armed, arrows / WASD / Space go to the snake and nowhere else.</summary>
    public SnakeKeyGate? SnakeGate { get; set; }

    /// <summary>Raised on the UI thread for a snake key while the gate is armed.</summary>
    public event Action<SnakeKey>? SnakeKeyPressed;

    /// <summary>Raised on the UI thread for Esc while the gate is armed (the snake gives the keys back).</summary>
    public event Action? SnakeReleased;

    /// <summary>While true, Esc (never swallowed) also raises <see cref="EscapePressed"/> (widget edit mode).</summary>
    public volatile bool EscapeWanted;

    /// <summary>Raised on the UI thread for Esc while <see cref="EscapeWanted"/>.</summary>
    public event Action? EscapePressed;

    public WinKeyListener(HookThread hookThread, Dispatcher ui)
    {
        _ui = ui;
        _hook = new KeyboardHookNative.Hook(hookThread, OnKey);
        Log.Info("win key hook installed");
    }

    // Hook thread. Tiny: detector + optional mask injection + a posted UI call.
    private bool OnKey(uint vk, bool down, bool injectedByUs)
    {
        if (vk == 0x1B && down && EscapeWanted && !injectedByUs)
            _ui.BeginInvoke(DispatcherPriority.Input, () => Safe(() => EscapePressed?.Invoke()));
        if (SnakeGate is { } gate && !injectedByUs)
        {
            var wasArmed = gate.Armed;
            if (wasArmed && vk == 0x1B && down) gate.Disarm(); // Esc gives the keys back, and is let through
            var swallow = vk != 0x1B && gate.Filter((int)vk, down, wasArmed ? ForegroundNative.Current() : 0, wasArmed && ModifierDown());
            if (wasArmed && !gate.Armed) _ui.BeginInvoke(DispatcherPriority.Input, () => Safe(() => SnakeReleased?.Invoke()));
            if (swallow)
            {
                if (down && gate.Map((int)vk) is { } key) _ui.BeginInvoke(DispatcherPriority.Input, () => Safe(() => SnakeKeyPressed?.Invoke(key)));
                return true;
            }
        }
        var v = _detector.OnKey(vk, down, injectedByUs);
        if ((v.OpenLauncher || v.OpenPanel) && SnakeGate is { Armed: true } armed)
        {
            armed.Disarm(); // the launcher / panel takes the keys
            _ui.BeginInvoke(DispatcherPriority.Input, () => Safe(() => SnakeReleased?.Invoke()));
        }
        if (v.InjectMaskFirst) KeyboardHookNative.InjectMaskKey();
        if (v.OpenLauncher) _ui.BeginInvoke(DispatcherPriority.Input, Raise);
        if (v.OpenPanel) _ui.BeginInvoke(DispatcherPriority.Input, RaisePanel);
        return v.Swallow;
    }

    private static bool ModifierDown() =>
        KeyboardHookNative.IsPhysicallyDown(0x11) || KeyboardHookNative.IsPhysicallyDown(0x12)
        || KeyboardHookNative.IsPhysicallyDown(0x5B) || KeyboardHookNative.IsPhysicallyDown(0x5C);

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Error("snake key failed", e); }
    }

    private void RaisePanel()
    {
        Log.Info("win+a/n -> panel");
        try { PanelRequested?.Invoke(); }
        catch (Exception e) { Log.Error("panel request failed", e); }
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
