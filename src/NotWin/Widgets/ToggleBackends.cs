using Microsoft.Win32;
using NotWin.Core.Widgets;
using NotWin.Native;
using NotWin.Panel;
using NotWin.Power;
using Windows.Devices.Radios;

namespace NotWin.Widgets;

/// <summary>
/// What a toggle tile switches (Plan 13). <see cref="Changed"/> may fire on any thread; the widget marshals it. Nothing
/// is changed except by <see cref="FlipAsync"/>, which only a click on the tile calls.
/// </summary>
public interface IToggleBackend : IDisposable
{
    Task<ToggleState> ReadAsync();

    Task FlipAsync();

    /// <summary>Where a click goes when the tile is unavailable or blocked by the machine-wide switch.</summary>
    string SettingsUri { get; }

    event Action? Changed;
}

public static class ToggleBackends
{
    /// <summary>The backend for a toggle id (<see cref="WidgetChoices.Toggles"/>).</summary>
    public static IToggleBackend For(string id, KeepAwakeService awake) => id switch
    {
        "bluetooth" => new RadioToggle(RadioKind.Bluetooth, "ms-settings:bluetooth"),
        "dark" => new DarkModeToggle(),
        "awake" => new KeepAwakeToggle(awake),
        "mic" => new PrivacyToggle("microphone"),
        "camera" => new PrivacyToggle("webcam"),
        _ => new RadioToggle(RadioKind.WiFi, "ms-settings:network-wifi"),
    };
}

/// <summary>Wi-Fi / Bluetooth radio, the same calls as quick settings.</summary>
public sealed class RadioToggle(RadioKind kind, string settingsUri) : IToggleBackend
{
    private Radio? _radio;
    private bool _disposed;

    public string SettingsUri => settingsUri;
    public event Action? Changed;

    public async Task<ToggleState> ReadAsync()
    {
        if (_radio is null)
        {
            _radio = await RadioService.FindAsync(kind);
            if (_radio is null) return ToggleState.Unavailable;
            if (_disposed) return ToggleState.Unavailable;
            _radio.StateChanged += OnStateChanged;
        }
        return _radio.State == RadioState.On ? ToggleState.On : ToggleState.Off;
    }

    public async Task FlipAsync()
    {
        var on = await ReadAsync() == ToggleState.On;
        await RadioService.SetAsync(kind, !on);
    }

    private void OnStateChanged(Radio sender, object args) => Changed?.Invoke();

    public void Dispose()
    {
        _disposed = true;
        if (_radio is not null) _radio.StateChanged -= OnStateChanged;
    }
}

/// <summary>Windows' app and system theme together (HKCU Personalize), then the theme-change broadcast.</summary>
public sealed class DarkModeToggle : IToggleBackend
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public DarkModeToggle() => SystemEvents.UserPreferenceChanged += OnPreference;

    public string SettingsUri => "ms-settings:colors";
    public event Action? Changed;

    public Task<ToggleState> ReadAsync()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        if (k is null) return Task.FromResult(ToggleState.Unavailable);
        return Task.FromResult(k.GetValue("AppsUseLightTheme") is int light && light == 0 ? ToggleState.On : ToggleState.Off);
    }

    public async Task FlipAsync()
    {
        var dark = await ReadAsync() == ToggleState.On;
        using (var k = Registry.CurrentUser.CreateSubKey(Key))
        {
            var light = dark ? 1 : 0; // dark on -> off means light
            k.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
            k.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
        }
        await Task.Run(() => SystemNative.BroadcastSettingChange("ImmersiveColorSet"));
        Changed?.Invoke();
    }

    private void OnPreference(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle) Changed?.Invoke();
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnPreference;
}

/// <summary>Keep awake: the app's one <see cref="KeepAwakeService"/>, so the tile and quick settings stay in step.</summary>
public sealed class KeepAwakeToggle : IToggleBackend
{
    private readonly KeepAwakeService _awake;

    public KeepAwakeToggle(KeepAwakeService awake)
    {
        _awake = awake;
        _awake.Changed += OnChanged;
    }

    public string SettingsUri => "ms-settings:powersleep";
    public event Action? Changed;

    public Task<ToggleState> ReadAsync() => Task.FromResult(_awake.On ? ToggleState.On : ToggleState.Off);

    public Task FlipAsync()
    {
        _awake.Set(!_awake.On);
        return Task.CompletedTask;
    }

    private void OnChanged() => Changed?.Invoke();

    public void Dispose() => _awake.Changed -= OnChanged;
}

/// <summary>
/// Mic / camera access for this user (<see cref="PrivacyConsent"/>): blocking writes Deny to both the apps and the
/// desktop-apps switch, allowing writes Allow to both. When the machine-wide switch is off nothing is written; the tile
/// opens Settings instead. A registry watch keeps the tile current when Settings changes it.
/// </summary>
public sealed class PrivacyToggle : IToggleBackend
{
    private const string DefaultRoot = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private readonly string _key;
    private readonly Func<string?> _machine;
    private readonly RegistryWatch _watch;

    /// <param name="userRoot">The consent store under HKCU (tests use a throwaway key).</param>
    /// <param name="machine">The machine-wide value (default: read from HKLM).</param>
    public PrivacyToggle(string capability, string? userRoot = null, Func<string?>? machine = null)
    {
        _key = (userRoot ?? DefaultRoot) + @"\" + capability;
        _machine = machine ?? (() => Registry.LocalMachine.OpenSubKey(DefaultRoot + @"\" + capability)?.GetValue("Value") as string);
        SettingsUri = capability == "webcam" ? "ms-settings:privacy-webcam" : "ms-settings:privacy-microphone";
        _watch = new RegistryWatch(_key);
        _watch.Changed += () => Changed?.Invoke();
    }

    public string SettingsUri { get; }
    public event Action? Changed;

    public Task<ToggleState> ReadAsync()
    {
        using var k = Registry.CurrentUser.OpenSubKey(_key);
        using var np = k?.OpenSubKey("NonPackaged");
        return Task.FromResult(PrivacyConsent.State(SafeMachine(), k?.GetValue("Value") as string, np?.GetValue("Value") as string));
    }

    public async Task FlipAsync()
    {
        var state = await ReadAsync();
        if (state is ToggleState.Blocked or ToggleState.Unavailable) return;
        var value = state == ToggleState.On ? "Deny" : "Allow";
        using (var k = Registry.CurrentUser.CreateSubKey(_key))
        {
            k.SetValue("Value", value, RegistryValueKind.String);
            using var np = k.CreateSubKey("NonPackaged");
            np.SetValue("Value", value, RegistryValueKind.String);
        }
        Changed?.Invoke();
    }

    private string? SafeMachine()
    {
        try { return _machine(); }
        catch (Exception) { return null; } // unreadable: treat as Windows' default (allowed)
    }

    public void Dispose() => _watch.Dispose();
}
