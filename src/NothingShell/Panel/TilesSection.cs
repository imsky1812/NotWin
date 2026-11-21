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
    private const string BtGlyph = "M7,7 L17,17 L12,22 L12,2 L17,7 L7,17";
    private readonly Tile _wifi = new("Wi-Fi", WifiGlyph);
    private readonly TileList _wifiList = new();
    private readonly Tile _bt = new("Bluetooth", BtGlyph);
    private readonly TileList _btList = new();
    private bool? _wifiOn;
    private bool? _btOn;

    public TilesSection(StackPanel host)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(_wifi);
        Grid.SetColumn(_bt, 2);
        grid.Children.Add(_bt);
        this.Grid = grid;
        host.Children.Add(grid);
        host.Children.Add(_wifiList);
        host.Children.Add(_btList);
        Host = host;

        _wifi.Toggled += () => _ = ToggleWifiAsync();
        _wifi.Expanded += () => _ = ExpandWifiAsync();
        _bt.Toggled += () => _ = ToggleBtAsync();
        _bt.Expanded += () => _ = ExpandBtAsync();
    }

    // ---- Bluetooth ----

    private async Task RefreshBtAsync()
    {
        _btOn = await BluetoothService.RadioOnAsync();
        var connected = _btOn == true ? (await BluetoothService.PairedAsync()).FirstOrDefault(d => d.Connected) : null;
        _bt.SetOn(_btOn == true, _btOn switch
        {
            null => "Unavailable",
            false => "Off",
            _ => connected?.Name ?? "On",
        });
    }

    private async Task ToggleBtAsync()
    {
        if (_btOn is null) { OpenSettings("ms-settings:bluetooth"); return; }
        await BluetoothService.SetRadioAsync(_btOn != true);
        await Task.Delay(400);
        await RefreshBtAsync();
    }

    private async Task ExpandBtAsync()
    {
        if (_btList.Visibility == Visibility.Visible) { _btList.Visibility = Visibility.Collapsed; return; }
        _wifiList.Visibility = Visibility.Collapsed; // one list at a time
        _btList.Clear();
        _btList.AddRow("Loading…", null, null, dim: true);
        _btList.Visibility = Visibility.Visible;
        var devices = _btOn == true ? await BluetoothService.PairedAsync() : [];
        _btList.Clear();
        if (_btOn != true) _btList.AddRow("Bluetooth is off", null, null, dim: true);
        foreach (var d in devices)
            _btList.AddRow(d.Name, BluetoothRules.ActionLabel(d), () => _ = ConnectAsync(d));
        _btList.AddRow("Bluetooth settings", "›", () => OpenSettings("ms-settings:bluetooth"), dim: true);
    }

    private async Task ConnectAsync(BtDevice d)
    {
        await BluetoothService.ToggleConnectionAsync(d);
        await Task.Delay(2000); // the stack needs a moment to (dis)connect
        await RefreshBtAsync();
        _btList.Visibility = Visibility.Collapsed;
    }

    /// <summary>The 2-column tile row (Bluetooth adds itself into column 2, Task 11).</summary>
    public Grid Grid { get; }

    public StackPanel Host { get; }

    /// <summary>Panel opening: refresh the tile states (cheap: radio state + connected SSID).</summary>
    public void Refresh()
    {
        _wifiList.Visibility = Visibility.Collapsed;
        _btList.Visibility = Visibility.Collapsed;
        _ = RefreshWifiAsync();
        _ = RefreshBtAsync();
    }

    /// <summary>Collapses the Wi-Fi list (another tile's list opened).</summary>
    public void CollapseWifi() => _wifiList.Visibility = Visibility.Collapsed;

    public event Action? WifiListOpened;

    private async Task RefreshWifiAsync()
    {
        _wifiOn = await RadioService.IsOnAsync(RadioKind.WiFi);
        // No scan here: a scan is location-gated (Windows' location prompt + indicator on every Win+A). The interface
        // state says "connected"; the SSID comes from the connection profile when Windows allows it.
        var (connected, ssid) = _wifiOn == true ? await Task.Run(ConnectedSsid) : (false, null);
        _wifi.SetOn(_wifiOn == true, WifiRules.TileSubline(_wifiOn, connected, ssid));
    }

    private static (bool Connected, string? Ssid) ConnectedSsid()
    {
        if (!WlanNative.IsConnected()) return (false, null);
        try
        {
            var p = global::Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            return (true, p?.IsWlanConnectionProfile == true ? p.WlanConnectionProfileDetails?.GetConnectedSsid() : null);
        }
        catch { return (true, null); }
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
        _btList.Visibility = Visibility.Collapsed; // one list at a time
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
