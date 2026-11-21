using System.ComponentModel;
using System.Windows.Threading;
using NothingShell.Core.Widgets;
using NothingShell.Dock;
using Windows.Devices.Enumeration;

namespace NothingShell.Widgets;

/// <summary>
/// Devices for the battery widget (spec §6.5): the laptop (from the tray status) and connected Bluetooth devices.
/// Paired Bluetooth devices come from a watcher on association endpoints (connected / disconnected); the level is the
/// PnP battery property on the device's nodes (the value Windows' Bluetooth settings show), read on connect and every
/// 2 minutes while something is connected. Devices that report no level are listed without one (spec §9). Device
/// names are never logged.
/// </summary>
/// <summary>What the battery widget reads: the current devices and a change signal.</summary>
public interface IBatterySource
{
    IReadOnlyList<BatteryDevice> Devices { get; }
    event Action? Changed;
}

public sealed class DeviceBatteryService : IBatterySource, IDisposable
{
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string ConnectedKey = "System.Devices.Aep.IsConnected";
    private const string ContainerKey = "System.Devices.Aep.ContainerId";
    private const string MajorClassKey = "System.Devices.Aep.Bluetooth.Cod.Major";
    private readonly TrayStatusService _tray;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly Dictionary<string, Bt> _bt = [];
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMinutes(2) };
    private DeviceWatcher? _watcher;
    private bool _disposed;

    private sealed class Bt
    {
        public required string Id;
        public string Name = "";
        public bool Connected;
        public Guid? Container;
        public string Icon = "speaker";
        public bool HasClass;
        public int? Percent;
    }

    public DeviceBatteryService(TrayStatusService tray)
    {
        _tray = tray;
        _tray.PropertyChanged += OnTray;
        _refresh.Tick += (_, _) => _ = RefreshLevelsAsync();
        try
        {
            // Paired Bluetooth devices, classic and LE, as association endpoints.
            const string aqs = "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" OR System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\") AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";
            _watcher = DeviceInformation.CreateWatcher(aqs, [ConnectedKey, ContainerKey, MajorClassKey], DeviceInformationKind.AssociationEndpoint);
            _watcher.Added += (_, d) => _ui.BeginInvoke(() => Upsert(d.Id, d.Name, d.Properties));
            _watcher.Updated += (_, u) => _ui.BeginInvoke(() => Upsert(u.Id, null, u.Properties));
            _watcher.Removed += (_, u) => _ui.BeginInvoke(() => { if (_bt.Remove(u.Id)) Raise(); });
            _watcher.Start();
        }
        catch (Exception e) { Log.Error("bluetooth battery watcher failed to start", e); }
    }

    public event Action? Changed;

    public IReadOnlyList<BatteryDevice> Devices
    {
        get
        {
            var list = new List<BatteryDevice>();
            if (_tray.HasBattery)
                list.Add(new BatteryDevice("laptop", "Laptop", "laptop", _tray.BatteryPercent, _tray.Charging || _tray.PluggedIn, true));
            foreach (var b in _bt.Values.Where(b => b.Connected))
                list.Add(new BatteryDevice(b.Id, b.Name, b.Icon, b.Percent, false, false));
            return list;
        }
    }

    private void OnTray(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrayStatusService.BatteryPercent) or nameof(TrayStatusService.Charging)
            or nameof(TrayStatusService.PluggedIn) or nameof(TrayStatusService.HasBattery))
            Raise();
    }

    private void Upsert(string id, string? name, IReadOnlyDictionary<string, object> props)
    {
        if (_disposed) return;
        if (!_bt.TryGetValue(id, out var b)) _bt[id] = b = new Bt { Id = id };
        if (!string.IsNullOrEmpty(name))
        {
            b.Name = name;
            if (!b.HasClass) b.Icon = IconFromName(name);
        }
        var was = b.Connected;
        if (props.TryGetValue(ConnectedKey, out var c) && c is bool connected) b.Connected = connected;
        if (props.TryGetValue(ContainerKey, out var g) && g is Guid container) b.Container = container;
        if (props.TryGetValue(MajorClassKey, out var m) && m is byte major && major != 0)
            (b.HasClass, b.Icon) = (true, major switch { 4 => "head", 5 => "mouse", 2 => "phone", _ => "speaker" }); // Audio/Video, Peripheral, Phone
        if (b.Connected && !was) _ = ReadLevelAsync(b);
        if (_bt.Values.Any(x => x.Connected)) _refresh.Start(); else _refresh.Stop();
        Raise();
    }

    // LE devices carry no class of device: a best guess from the name (in memory only, never logged).
    private static string IconFromName(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("mouse") || n.Contains("keyboard") || n.Contains("mx ")) return "mouse";
        if (n.Contains("bud") || n.Contains("pod") || n.Contains("ear") || n.Contains("head") || n.Contains("phone ")) return "head";
        return "speaker";
    }

    private async Task RefreshLevelsAsync()
    {
        foreach (var b in _bt.Values.Where(x => x.Connected).ToList()) await ReadLevelAsync(b);
    }

    private async Task ReadLevelAsync(Bt b)
    {
        if (b.Container is not { } container) return;
        try
        {
            var aqs = $"System.Devices.ContainerId:=\"{{{container}}}\"";
            var nodes = await DeviceInformation.FindAllAsync(aqs, [BatteryKey], DeviceInformationKind.Device);
            int? level = null;
            foreach (var n in nodes)
                if (n.Properties.TryGetValue(BatteryKey, out var v) && v is byte pct) { level = pct; break; }
            if (level != b.Percent)
            {
                b.Percent = level;
                Raise();
            }
        }
        catch (Exception e) { Log.Error("reading a bluetooth battery level failed", e); }
    }

    private void Raise() => Changed?.Invoke();

    public void Dispose()
    {
        _disposed = true;
        _refresh.Stop();
        _tray.PropertyChanged -= OnTray;
        try { if (_watcher is { Status: DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted }) _watcher.Stop(); }
        catch { }
    }
}
