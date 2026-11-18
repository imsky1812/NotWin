using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using NothingShell.Core.Notch;
using NothingShell.Dock;

namespace NothingShell.Notch;

/// <summary>
/// Drives the notch: feeds the state machine (launcher opening, media, notifications, full screen) and shows what it
/// says. No polling: one timer is armed only for the end of a transient, and the clock follows the tray's minute tick.
/// </summary>
public sealed class NotchController : IDisposable
{
    private readonly NotchStateMachine _machine = new();
    private readonly NotchWindow _window = new();
    private readonly TrayStatusService _tray;
    private readonly DispatcherTimer _transient;
    private NotchState _last = (NotchState)(-1);

    public NotchController(TrayStatusService tray)
    {
        _tray = tray;
        _transient = new DispatcherTimer();
        _transient.Tick += (_, _) => { _transient.Stop(); Render(); };
        _tray.PropertyChanged += OnTrayChanged;
        _window.Initialize();
        UpdateClockAndBattery();
        Render();
    }

    public void Opening(string name)
    {
        _machine.Opening(name, DateTimeOffset.Now);
        if (_machine.OpeningName == name) _window.SetOpening(name);
        Render();
    }

    public void Notify(NotchNotification n, ImageSource? icon)
    {
        _machine.Notify(n, DateTimeOffset.Now);
        if (_machine.CurrentNotification == n) _window.SetNotification(n, icon);
        Render();
    }

    public void SetMusic(bool playing, ImageSource? art)
    {
        _machine.SetMusicPlaying(playing);
        _window.SetMusicArt(art);
        Render();
    }

    public void SetFullscreen(bool fullscreen)
    {
        _machine.SetFullscreen(fullscreen);
        Render();
    }

    private void OnTrayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrayStatusService.Time) or nameof(TrayStatusService.BatteryPercent)
            or nameof(TrayStatusService.Charging) or nameof(TrayStatusService.HasBattery))
            UpdateClockAndBattery();
    }

    private void UpdateClockAndBattery()
    {
        _window.SetClock(DateTime.Now.ToString("HH:mm"));
        _window.SetBattery(_tray.HasBattery, _tray.BatteryPercent, _tray.Charging);
    }

    private void Render()
    {
        var now = DateTimeOffset.Now;
        var state = _machine.StateAt(now);
        _window.ShowState(state);
        if (state != _last) { Log.Info($"notch state={state}"); _last = state; }

        _transient.Stop();
        if (_machine.NextChangeAt(now) is { } at)
        {
            _transient.Interval = at - now + TimeSpan.FromMilliseconds(15);
            _transient.Start();
        }
    }

    public void Dispose()
    {
        _transient.Stop();
        _tray.PropertyChanged -= OnTrayChanged;
        try { _window.Close(); } catch { }
    }
}
