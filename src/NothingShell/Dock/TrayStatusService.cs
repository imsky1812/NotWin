using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Microsoft.Win32;
using NothingShell.Native;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;
using Windows.System.Power;

namespace NothingShell.Dock;

/// <summary>
/// The values behind the dock's tray: clock, Wi-Fi, volume, battery. Everything is event driven except the clock,
/// which re-arms one DispatcherTimer to the next whole minute (so it never drifts and is the only idle timer).
/// Events arrive on thread-pool / audio threads and are marshalled to the UI thread; properties change on the UI thread only.
/// </summary>
public sealed class TrayStatusService : INotifyPropertyChanged, IDisposable
{
    private const int LowBatteryPercent = 20;
    private const double BatteryFillMax = 10; // inner width of the 16x9 battery glyph, DIP

    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _clock;
    private readonly List<Radio> _radios = [];
    private AudioVolumeWatcher? _audio;
    private NetworkStatusChangedEventHandler? _netHandler;
    private bool _started;
    private bool _disposed;

    private string _time = "";
    private string _day = "";
    private bool _wifiConnected;
    private bool _wifiEnabled = true;
    private int _volumePercent;
    private bool _muted;
    private bool _hasBattery;
    private int _batteryPercent;
    private bool _charging;

    public TrayStatusService()
    {
        _clock = new DispatcherTimer(DispatcherPriority.Normal, _ui);
        _clock.Tick += (_, _) => TickClock();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>"HH:mm".</summary>
    public string Time { get => _time; private set => Set(ref _time, value); }

    /// <summary>"ddd d", for example "Tue 17".</summary>
    public string Day { get => _day; private set => Set(ref _day, value); }

    /// <summary>Connected through a Wi-Fi profile.</summary>
    public bool WifiConnected { get => _wifiConnected; private set => Set(ref _wifiConnected, value); }

    /// <summary>The Wi-Fi radio is on (false: switched off / airplane mode).</summary>
    public bool WifiEnabled { get => _wifiEnabled; private set => Set(ref _wifiEnabled, value); }

    public int VolumePercent
    {
        get => _volumePercent;
        private set { if (Set(ref _volumePercent, value)) OnChanged(nameof(VolumeLevel)); }
    }

    public bool Muted
    {
        get => _muted;
        private set { if (Set(ref _muted, value)) OnChanged(nameof(VolumeLevel)); }
    }

    /// <summary>Glyph level: 0 muted or silent, 1 low, 2 high.</summary>
    public int VolumeLevel => _muted || _volumePercent == 0 ? 0 : _volumePercent < 50 ? 1 : 2;

    /// <summary>False on a desktop PC: the battery glyph is hidden.</summary>
    public bool HasBattery { get => _hasBattery; private set => Set(ref _hasBattery, value); }

    public int BatteryPercent
    {
        get => _batteryPercent;
        private set
        {
            if (!Set(ref _batteryPercent, value)) return;
            OnChanged(nameof(BatteryFillWidth));
            OnChanged(nameof(BatteryLow));
        }
    }

    public bool Charging
    {
        get => _charging;
        private set { if (Set(ref _charging, value)) OnChanged(nameof(BatteryLow)); }
    }

    /// <summary>Width of the fill inside the battery outline (0-10 DIP).</summary>
    public double BatteryFillWidth => BatteryFillMax * Math.Clamp(_batteryPercent, 0, 100) / 100.0;

    /// <summary>Below 20 % and not charging: the fill turns red.</summary>
    public bool BatteryLow => _batteryPercent < LowBatteryPercent && !_charging;

    public void Start()
    {
        if (_started) return;
        _started = true;
        TickClock();
        StartBattery();
        StartWifi();
        StartAudio();
        SystemEvents.TimeChanged += OnSystemTimeChanged;
        Log.Info($"tray initial: time={Time} day={Day} wifiConnected={WifiConnected} wifiEnabled={WifiEnabled} " +
                 $"volume={VolumePercent} muted={Muted} battery={(HasBattery ? BatteryPercent + "%" : "none")} charging={Charging}");
    }

    // ---- clock ----

    private void TickClock()
    {
        var now = DateTime.Now;
        Time = now.ToString("HH:mm", CultureInfo.CurrentCulture);
        Day = now.ToString("ddd d", CultureInfo.CurrentCulture);
        // Re-arm to the next whole minute from the real time: no drift, and a late tick (sleep, load) self-corrects.
        var next = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind).AddMinutes(1);
        _clock.Stop();
        _clock.Interval = next - now + TimeSpan.FromMilliseconds(20);
        _clock.Start();
    }

    private void OnSystemTimeChanged(object? sender, EventArgs e) => _ui.BeginInvoke(TickClock);

    // ---- battery ----

    private void StartBattery()
    {
        try
        {
            ReadBattery();
            PowerManager.RemainingChargePercentChanged += OnPowerChanged;
            PowerManager.BatteryStatusChanged += OnPowerChanged;
        }
        catch (Exception e) { Log.Error("battery status unavailable", e); }
    }

    private void OnPowerChanged(object? sender, object e) => _ui.BeginInvoke(ReadBattery);

    private void ReadBattery()
    {
        if (_disposed) return;
        try
        {
            var status = PowerManager.BatteryStatus;
            HasBattery = status != BatteryStatus.NotPresent;
            BatteryPercent = Math.Clamp(PowerManager.RemainingChargePercent, 0, 100);
            Charging = status == BatteryStatus.Charging;
        }
        catch (Exception e) { Log.Error("battery read failed", e); }
    }

    // ---- Wi-Fi ----

    private void StartWifi()
    {
        try
        {
            ReadWifiConnection();
            _netHandler = _ => _ui.BeginInvoke(ReadWifiConnection);
            NetworkInformation.NetworkStatusChanged += _netHandler;
        }
        catch (Exception e) { Log.Error("network status unavailable", e); }
        _ = StartRadiosAsync();
    }

    private void ReadWifiConnection()
    {
        if (_disposed) return;
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            WifiConnected = profile is { IsWlanConnectionProfile: true } &&
                            profile.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None;
        }
        catch (Exception e) { Log.Error("wifi read failed", e); }
    }

    // The radio on/off switch (Wi-Fi toggle in quick settings, airplane mode, a hardware key).
    private async Task StartRadiosAsync()
    {
        try
        {
            foreach (var radio in await Radio.GetRadiosAsync())
            {
                if (radio.Kind != RadioKind.WiFi) continue;
                if (_disposed) return;
                _radios.Add(radio);
                radio.StateChanged += OnRadioStateChanged;
            }
            ReadWifiEnabled();
            Log.Info($"tray wifi radios={_radios.Count} enabled={WifiEnabled}");
        }
        catch (Exception e) { Log.Error("wifi radio state unavailable (assuming on)", e); }
    }

    private void OnRadioStateChanged(Radio sender, object args) => _ui.BeginInvoke(ReadWifiEnabled);

    private void ReadWifiEnabled()
    {
        if (_disposed || _radios.Count == 0) return;
        WifiEnabled = _radios.Any(r => r.State == RadioState.On);
    }

    // ---- volume ----

    private void StartAudio()
    {
        var audio = new AudioVolumeWatcher { OnError = (m, e) => Log.Error(m, e) };
        if (!audio.Start()) { audio.Dispose(); return; }
        _audio = audio;
        audio.VolumeChanged += (p, m) => _ui.BeginInvoke(() => ApplyVolume(p, m));
        // The default device changed: re-bind on the UI thread (never from inside the callback).
        audio.DefaultDeviceChanged += () => _ui.BeginInvoke(RebindAudio);
        RebindAudio();
    }

    private void RebindAudio()
    {
        if (_disposed || _audio is null) return;
        var v = _audio.Rebind();
        if (v is { } vol) ApplyVolume(vol.Percent, vol.Muted);
        else ApplyVolume(0, true); // no output device: show the muted glyph
    }

    private void ApplyVolume(int percent, bool muted)
    {
        if (_disposed) return;
        VolumePercent = percent;
        Muted = muted;
    }

    // ---- plumbing ----

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        return true;
    }

    private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clock.Stop();
        try
        {
            SystemEvents.TimeChanged -= OnSystemTimeChanged;
            PowerManager.RemainingChargePercentChanged -= OnPowerChanged;
            PowerManager.BatteryStatusChanged -= OnPowerChanged;
            if (_netHandler is not null) NetworkInformation.NetworkStatusChanged -= _netHandler;
            foreach (var r in _radios) r.StateChanged -= OnRadioStateChanged;
            _radios.Clear();
            _audio?.Dispose();
            _audio = null;
        }
        catch (Exception e) { Log.Error("tray dispose failed", e); }
    }
}
