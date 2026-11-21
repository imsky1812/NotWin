using System.Runtime.InteropServices;

namespace NotWin.Native;

/// <summary>SetThreadExecutionState: keeps the system and display awake while set (per calling thread).</summary>
public static class PowerRequestNative
{
    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001, EsDisplayRequired = 0x00000002;

    /// <summary>On: no sleep and no screen-off until cleared. Off: back to normal. Call on the same (UI) thread.</summary>
    public static bool KeepAwake(bool on) =>
        SetThreadExecutionState(on ? EsContinuous | EsSystemRequired | EsDisplayRequired : EsContinuous) != 0;

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
