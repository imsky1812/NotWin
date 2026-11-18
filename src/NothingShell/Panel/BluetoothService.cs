using NothingShell.Core.Panel;
using NothingShell.Native;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace NothingShell.Panel;

/// <summary>Paired Bluetooth devices and connect / disconnect for the panel (spec §6.3). Device names are never logged.</summary>
public static class BluetoothService
{
    public static Task<bool?> RadioOnAsync() => RadioService.IsOnAsync(RadioKind.Bluetooth);

    public static Task<bool> SetRadioAsync(bool on) => RadioService.SetAsync(RadioKind.Bluetooth, on);

    public static async Task<IReadOnlyList<BtDevice>> PairedAsync()
    {
        var result = new List<BtDevice>();
        try
        {
            var infos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
            foreach (var info in infos)
            {
                try
                {
                    using var dev = await BluetoothDevice.FromIdAsync(info.Id);
                    if (dev is null) continue;
                    var audio = dev.ClassOfDevice?.MajorClass == BluetoothMajorClass.AudioVideo;
                    result.Add(new BtDevice(info.Id, dev.Name, dev.ConnectionStatus == BluetoothConnectionStatus.Connected, audio));
                }
                catch { /* a device that vanished mid-enumeration */ }
            }
        }
        catch (Exception e) { Log.Error("listing Bluetooth devices failed", e); }
        return BluetoothRules.Order(result);
    }

    /// <summary>Connects or disconnects an audio device; anything else (or a failure) opens Windows' Bluetooth settings.</summary>
    public static async Task<bool> ToggleConnectionAsync(BtDevice d)
    {
        var ok = d.IsAudio && await Task.Run(() => BluetoothAudioNative.SetConnected(d.Name, !d.Connected));
        Log.Info($"bluetooth {(d.Connected ? "disconnect" : "connect")} audio={d.IsAudio} ok={ok}");
        if (!ok) TilesSection.OpenSettings("ms-settings:bluetooth");
        return ok;
    }
}
