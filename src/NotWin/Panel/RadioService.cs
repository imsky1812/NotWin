using Windows.Devices.Radios;

namespace NotWin.Panel;

/// <summary>Wi-Fi / Bluetooth radio on-off (Windows.Devices.Radios; the package declares the "radios" capability).</summary>
public static class RadioService
{
    /// <summary>True/false for on/off, null when the machine has no such radio or access is denied.</summary>
    public static async Task<bool?> IsOnAsync(RadioKind kind)
    {
        var r = await FindAsync(kind);
        return r is null ? null : r.State == RadioState.On;
    }

    public static async Task<bool> SetAsync(RadioKind kind, bool on)
    {
        var r = await FindAsync(kind);
        if (r is null) return false;
        try
        {
            var result = await r.SetStateAsync(on ? RadioState.On : RadioState.Off);
            Log.Info($"radio {kind} -> {(on ? "on" : "off")}: {result}");
            return result == RadioAccessStatus.Allowed;
        }
        catch (Exception e)
        {
            Log.Error($"radio {kind} toggle failed", e);
            return false;
        }
    }

    private static async Task<Radio?> FindAsync(RadioKind kind)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return null;
            return (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == kind);
        }
        catch (Exception e)
        {
            Log.Error("radio access failed", e);
            return null;
        }
    }
}
