namespace NothingShell.Core.Panel;

/// <summary>A paired Bluetooth device as the panel lists it.</summary>
public sealed record BtDevice(string Id, string Name, bool Connected, bool IsAudio);

public static class BluetoothRules
{
    /// <summary>Connected devices first, then alphabetical; unnamed devices dropped.</summary>
    public static IReadOnlyList<BtDevice> Order(IEnumerable<BtDevice> devices) =>
        devices
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .OrderByDescending(d => d.Connected)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
