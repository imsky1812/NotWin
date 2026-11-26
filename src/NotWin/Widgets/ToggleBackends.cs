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
    public static IToggleBackend For(string id, KeepAwakeService awake, DarkModeWallpaperHost? wallpaper = null) => id switch
    {
        "bluetooth" => new RadioToggle(RadioKind.Bluetooth, "ms-settings:bluetooth"),
        "dark" => new DarkModeToggle(wallpaper),
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
/// <summary>
/// What the Dark mode tile needs to swap the wallpaper (Plan 13 follow-up): the current wallpaper and a way to set it,
/// the remembered one (settings, per PC), NotWin's dark dot wallpaper and a file-exists check.
/// </summary>
public sealed record DarkModeWallpaperHost(Func<string?> Current, Action<string> Apply, Func<string?> Remembered, Action<string?> Remember,
    string DarkPath, Func<string, bool> Exists);

/// <summary>
/// Windows' app and system theme together (HKCU Personalize), then the theme-change broadcast. With a
/// <see cref="DarkModeWallpaperHost"/>, turning dark also switches to NotWin's dark dot wallpaper and turning light puts
/// the previous one back (<see cref="DarkModeWallpaper"/>); only this click ever changes the wallpaper.
/// </summary>
public sealed class DarkModeToggle : IToggleBackend
{
    private const string DefaultKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private readonly DarkModeWallpaperHost? _wallpaper;
    private readonly string _key;
    private readonly bool _broadcast;

    /// <param name="themeKey">The Personalize key under HKCU (tests use a throwaway key).</param>
    /// <param name="broadcast">Tell running apps the theme changed (off in tests).</param>
    public DarkModeToggle(DarkModeWallpaperHost? wallpaper = null, string themeKey = DefaultKey, bool broadcast = true)
    {
        _wallpaper = wallpaper;
        _key = themeKey;
        _broadcast = broadcast;
        SystemEvents.UserPreferenceChanged += OnPreference;
    }

    public string SettingsUri => "ms-settings:colors";
    public event Action? Changed;

    public Task<ToggleState> ReadAsync()
    {
        using var k = Registry.CurrentUser.OpenSubKey(_key);
        if (k is null) return Task.FromResult(ToggleState.Unavailable);
        return Task.FromResult(k.GetValue("AppsUseLightTheme") is int light && light == 0 ? ToggleState.On : ToggleState.Off);
    }

    public async Task FlipAsync()
    {
        var dark = await ReadAsync() == ToggleState.On;
        using (var k = Registry.CurrentUser.CreateSubKey(_key))
        {
            var light = dark ? 1 : 0; // dark on -> off means light
            k.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
            k.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
        }
        if (_broadcast) await Task.Run(() => SystemNative.BroadcastSettingChange("ImmersiveColorSet"));
        if (_wallpaper is { } w)
        {
            var (apply, remember) = DarkModeWallpaper.OnFlip(!dark, w.Current(), w.Remembered(), w.DarkPath, w.Exists);
            w.Remember(remember);
            if (apply is not null)
            {
                var path = apply;
                await Task.Run(() => w.Apply(path));
            }
            Log.Info($"dark mode -> {(!dark ? "on" : "off")}, wallpaper changed={apply is not null}");
        }
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
    private readonly RegistryWatch _watch, _watchDesktopApps;

    /// <param name="userRoot">The consent store under HKCU (tests use a throwaway key).</param>
    /// <param name="machine">The machine-wide value (default: read from HKLM).</param>
    public PrivacyToggle(string capability, string? userRoot = null, Func<string?>? machine = null)
    {
        _key = (userRoot ?? DefaultRoot) + @"\" + capability;
        _machine = machine ?? (() => Registry.LocalMachine.OpenSubKey(DefaultRoot + @"\" + capability)?.GetValue("Value") as string);
        SettingsUri = capability == "webcam" ? "ms-settings:privacy-webcam" : "ms-settings:privacy-microphone";
        // The two switches only: the key's own values and NonPackaged's (not the per-app "last used" subkeys).
        _watch = new RegistryWatch(_key);
        _watch.Changed += () => Changed?.Invoke();
        _watchDesktopApps = new RegistryWatch(_key + @"\NonPackaged");
        _watchDesktopApps.Changed += () => Changed?.Invoke();
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

    public void Dispose()
    {
        _watch.Dispose();
        _watchDesktopApps.Dispose();
    }
}
