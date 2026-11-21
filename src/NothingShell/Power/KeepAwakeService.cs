using NothingShell.Native;

namespace NothingShell.Power;

/// <summary>
/// Keep awake (spec §6.4): prevents sleep and screen-off while on. Not saved: every launch starts with it off, and it
/// is cleared on exit. Use from the UI thread (the execution state belongs to the calling thread).
/// </summary>
public sealed class KeepAwakeService : IDisposable
{
    public bool On { get; private set; }

    public event Action? Changed;

    public void Set(bool on)
    {
        if (on == On) return;
        if (!PowerRequestNative.KeepAwake(on)) { Log.Info("keep awake: SetThreadExecutionState failed"); return; }
        On = on;
        Log.Info($"keep awake={on}");
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (On) PowerRequestNative.KeepAwake(false);
        On = false;
    }
}
