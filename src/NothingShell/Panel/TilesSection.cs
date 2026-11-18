using System.Windows;
using System.Windows.Controls;
using NothingShell.Core.Panel;
using NothingShell.Native;
using Windows.Devices.Radios;

namespace NothingShell.Panel;

/// <summary>
/// The Wi-Fi and Bluetooth tiles (2 columns, 8 gap) and their expandable lists (spec §6.3). Tapping a tile toggles
/// its radio; ▸ expands one list at a time. Scans run only on expand; nothing polls while the panel is closed.
/// </summary>
public sealed class TilesSection
{
    private const string WifiGlyph = "M2,9 A15,15 0 0 1 22,9 M5.5,12.5 A10,10 0 0 1 18.5,12.5 M9,16 A5,5 0 0 1 15,16 M12,19.5 L12,19.6";
    private readonly Tile _wifi = new("Wi-Fi", WifiGlyph);
    private readonly TileList _wifiList = new();
    private bool? _wifiOn;

    public TilesSection(StackPanel host)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(_wifi);
        Grid = grid;
        host.Children.Add(grid);
        host.Children.Add(_wifiList);
        Host = host;

        _wifi.Toggled += () => _ = ToggleWifiAsync();
        _wifi.Expanded += () => _ = ExpandWifiAsync();
    }

    /// <summary>The 2-column tile row (Bluetooth adds itself into column 2, Task 11).</summary>
    public Grid Grid { get; }

    public StackPanel Host { get; }

    /// <summary>Panel opening: refresh the tile states (cheap: radio state + connected SSID).</summary>
    public void Refresh()
    {
        _wifiList.Visibility = Visibility.Collapsed;
        _ = RefreshWifiAsync();
    }

    /// <summary>Collapses the Wi-Fi list (another tile's list opened).</summary>
    public void CollapseWifi() => _wifiList.Visibility = Visibility.Collapsed;

    public event Action? WifiListOpened;

    private async Task RefreshWifiAsync()
    {
        _wifiOn = await RadioService.IsOnAsync(RadioKind.WiFi);
        var connected = _wifiOn == true ? await Task.Run(() => WifiRules.Order(WlanNative.GetNetworks().Networks).FirstOrDefault(n => n.Connected)) : null;
        _wifi.SetOn(_wifiOn == true, _wifiOn switch
        {
            null => "Unavailable",
            false => "Off",
            _ => connected?.Ssid ?? "Not connected",
        });
    }

    private async Task ToggleWifiAsync()
    {
        if (_wifiOn is null) { OpenSettings("ms-settings:network-wifi"); return; }
        await RadioService.SetAsync(RadioKind.WiFi, _wifiOn != true);
        await Task.Delay(400); // the adapter reports its new state shortly after
        await RefreshWifiAsync();
    }

    private async Task ExpandWifiAsync()
    {
        if (_wifiList.Visibility == Visibility.Visible) { _wifiList.Visibility = Visibility.Collapsed; return; }
        WifiListOpened?.Invoke();
        _wifiList.Clear();
        _wifiList.AddRow("Searching…", null, null, dim: true);
        _wifiList.Visibility = Visibility.Visible;

        var (status, nets) = await Task.Run(WlanNative.GetNetworks);
        _wifiList.Clear();
        if (WifiRules.Hint(status) is { } hint)
        {
            var target = status == WifiScanStatus.LocationDenied ? "ms-settings:privacy-location" : "ms-settings:network-wifi";
            _wifiList.AddRow(hint, "Open", () => OpenSettings(target));
        }
        else
        {
            foreach (var n in WifiRules.Order(nets).Take(8))
            {
                var action = WifiRules.ActionFor(n);
                var label = action switch
                {
                    WifiAction.None => "Connected",
                    WifiAction.OpenSettings => "Join ›",
                    _ => "Join",
                };
                _wifiList.AddRow(n.Ssid, label, action == WifiAction.None ? null : () => _ = JoinAsync(n, action));
            }
        }
        _wifiList.AddRow("More Wi-Fi settings", "›", () => OpenSettings("ms-settings:network-wifi"), dim: true);
        Log.Info($"wifi list: status={status} rows={nets.Count}");
    }

    private async Task JoinAsync(WifiNetwork n, WifiAction action)
    {
        bool ok;
        switch (action)
        {
            case WifiAction.OpenSettings: OpenSettings("ms-settings:network-wifi"); return;
            case WifiAction.Connect: ok = await Task.Run(() => WlanNative.Connect(n.Ssid)); break;
            case WifiAction.ConnectWithNewOpenProfile: ok = await Task.Run(() => WlanNative.AddProfileAndConnect(WifiRules.OpenProfileXml(n.Ssid), n.Ssid)); break;
            default: return;
        }
        Log.Info($"wifi join ({action}) ok={ok}");
        await Task.Delay(1500);
        await RefreshWifiAsync();
        _wifiList.Visibility = Visibility.Collapsed;
    }

    internal static void OpenSettings(string uri) => ShellLinkNative.Open(uri);
}
