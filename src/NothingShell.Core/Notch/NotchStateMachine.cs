namespace NothingShell.Core.Notch;

public enum NotchState { Hidden, Resting, Music, Timer, Notification, Opening }

/// <summary>A notification as the notch shows it (app id for the icon, app name, one line of text).</summary>
public readonly record struct NotchNotification(string AppId, string AppName, string Text);

/// <summary>
/// Which notch state is showing (spec §6.4): priority notification &gt; timer &gt; music &gt; resting, plus the brief
/// "Opening ‹name›…" after a launch (below notification). Notifications show for 3.6 s; a newer one replaces the
/// current one and restarts the time (no queue). While a full-screen app is in front the notch is hidden and
/// transients are dropped, so nothing replays afterwards. Pure: the caller passes the clock.
/// </summary>
public sealed class NotchStateMachine
{
    public static readonly TimeSpan NotificationTime = TimeSpan.FromMilliseconds(3600);
    public static readonly TimeSpan OpeningTime = TimeSpan.FromMilliseconds(1600);

    private DateTimeOffset _notificationUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _openingUntil = DateTimeOffset.MinValue;
    private bool _music;
    private bool _timer;
    private bool _fullscreen;

    public NotchNotification? CurrentNotification { get; private set; }
    public string? OpeningName { get; private set; }

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

    public void SetFullscreen(bool fullscreen)
    {
        _fullscreen = fullscreen;
        if (!fullscreen) return;
        _notificationUntil = DateTimeOffset.MinValue;
        _openingUntil = DateTimeOffset.MinValue;
        CurrentNotification = null;
        OpeningName = null;
    }

    public NotchState StateAt(DateTimeOffset now)
    {
        if (_fullscreen) return NotchState.Hidden;
        if (now < _notificationUntil) return NotchState.Notification;
        if (now < _openingUntil) return NotchState.Opening;
        if (_timer) return NotchState.Timer;
        if (_music) return NotchState.Music;
        return NotchState.Resting;
    }

    /// <summary>When the current transient (notification / opening) ends, or null if none is showing.</summary>
    public DateTimeOffset? NextChangeAt(DateTimeOffset now)
    {
        if (_fullscreen) return null;
        if (now < _notificationUntil) return _notificationUntil;
        if (now < _openingUntil) return _openingUntil;
        return null;
    }
}
