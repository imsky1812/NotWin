namespace NothingShell.Core.Notch;

public enum NotchState { Hidden, Resting, Music, Timer, Notification, Opening, Power, Open }

/// <summary>A notification as the notch shows it (app id for the icon, app name, one line of text).</summary>
public readonly record struct NotchNotification(string AppId, string AppName, string Text);

/// <summary>
/// Which notch state is showing (spec §6.4): priority notification &gt; timer &gt; music &gt; resting, plus the brief
/// "Opening ‹name›…" after a launch (below notification) and the 2.4 s plugged-in / unplugged card (between the two). Notifications show for 3.6 s; a newer one replaces the
/// current one and restarts the time (no queue). While a full-screen app is in front the notch is hidden and
/// transients are dropped, so nothing replays afterwards. Pure: the caller passes the clock.
/// </summary>
public sealed class NotchStateMachine
{
    public static readonly TimeSpan NotificationTime = TimeSpan.FromMilliseconds(3600);
    public static readonly TimeSpan OpeningTime = TimeSpan.FromMilliseconds(1600);

    /// <summary>Plugged in / unplugged (user request 2026-11-19): below a notification, above "Opening…".</summary>
    public static readonly TimeSpan PowerTime = TimeSpan.FromMilliseconds(2400);

    private DateTimeOffset _notificationUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _openingUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _powerUntil = DateTimeOffset.MinValue;
    private bool _music;
    private bool _timer;
    private bool _fullscreen;
    private bool _open;

    public NotchNotification? CurrentNotification { get; private set; }
    public string? OpeningName { get; private set; }

    public (bool Charging, int Percent)? CurrentPower { get; private set; }

    public void PowerChanged(bool charging, int percent, DateTimeOffset now)
    {
        if (_fullscreen || now < _notificationUntil) return;
        CurrentPower = (charging, Math.Clamp(percent, 0, 100));
        _powerUntil = now + PowerTime;
        _openingUntil = DateTimeOffset.MinValue; // the power change replaces a showing "Opening…"
    }

    public void Notify(NotchNotification n, DateTimeOffset now)
    {
        if (_fullscreen) return; // stays in the panel only (spec: hidden while full screen)
        CurrentNotification = n;
        _notificationUntil = now + NotificationTime;
    }

    public void Opening(string name, DateTimeOffset now)
    {
        if (_fullscreen || now < _notificationUntil) return; // never interrupts a notification
        OpeningName = name;
        _openingUntil = now + OpeningTime;
    }

    public void SetMusicPlaying(bool playing) => _music = playing;

    public void SetTimerRunning(bool running) => _timer = running;

    /// <summary>The hover dashboard is open (Plan 4): it beats every live state; transients keep their own clocks.</summary>
    public void SetOpen(bool open) => _open = open;

    public void SetFullscreen(bool fullscreen)
    {
        _fullscreen = fullscreen;
        if (!fullscreen) return;
        _open = false;
        _notificationUntil = DateTimeOffset.MinValue;
        _openingUntil = DateTimeOffset.MinValue;
        _powerUntil = DateTimeOffset.MinValue;
        CurrentNotification = null;
        OpeningName = null;
        CurrentPower = null;
    }

    public NotchState StateAt(DateTimeOffset now)
    {
        if (_fullscreen) return NotchState.Hidden;
        if (_open) return NotchState.Open;
        if (now < _notificationUntil) return NotchState.Notification;
        if (now < _powerUntil) return NotchState.Power;
        if (now < _openingUntil) return NotchState.Opening;
        if (_timer) return NotchState.Timer;
        if (_music) return NotchState.Music;
        return NotchState.Resting;
    }

    /// <summary>When the current transient (notification / power / opening) ends, or null if none is showing.</summary>
    public DateTimeOffset? NextChangeAt(DateTimeOffset now)
    {
        if (_fullscreen) return null;
        if (now < _notificationUntil) return _notificationUntil;
        if (now < _powerUntil) return _powerUntil;
        if (now < _openingUntil) return _openingUntil;
        return null;
    }
}
