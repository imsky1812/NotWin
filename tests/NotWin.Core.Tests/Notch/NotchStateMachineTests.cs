using NotWin.Core.Notch;

namespace NotWin.Core.Tests.Notch;

public sealed class NotchStateMachineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly NotchNotification N1 = new("app.one", "One", "hello");
    private static readonly NotchNotification N2 = new("app.two", "Two", "world");
    private static DateTimeOffset Ms(int ms) => T0.AddMilliseconds(ms);

    [Fact]
    public void Resting_ByDefault() => Assert.Equal(NotchState.Resting, new NotchStateMachine().StateAt(T0));

    [Fact]
    public void Music_OnlyWhilePlaying()
    {
        var m = new NotchStateMachine();
        m.SetMusicPlaying(true);
        Assert.Equal(NotchState.Music, m.StateAt(T0));
        m.SetMusicPlaying(false);
        Assert.Equal(NotchState.Resting, m.StateAt(T0));
    }

    [Fact]
    public void Notification_ShowsFor3600ms_ThenFallsBack()
    {
        var m = new NotchStateMachine();
        m.SetMusicPlaying(true);
        m.Notify(N1, T0);
        Assert.Equal(NotchState.Notification, m.StateAt(Ms(3599)));
        Assert.Equal(N1, m.CurrentNotification);
        Assert.Equal(NotchState.Music, m.StateAt(Ms(3600)));
        Assert.Equal(TimeSpan.FromMilliseconds(3600), NotchStateMachine.NotificationTime);
    }

    [Fact]
    public void NewNotification_ReplacesCurrentAndRestartsTimer()
    {
        var m = new NotchStateMachine();
        m.Notify(N1, T0);
        m.Notify(N2, Ms(2000));
        Assert.Equal(N2, m.CurrentNotification);
        Assert.Equal(NotchState.Notification, m.StateAt(Ms(5599)));
        Assert.Equal(NotchState.Resting, m.StateAt(Ms(5600)));
    }

    [Fact]
    public void Notification_BeatsTimerAndMusic()
    {
        var m = new NotchStateMachine();
        m.SetMusicPlaying(true);
        m.SetTimerRunning(true);
        m.Notify(N1, T0);
        Assert.Equal(NotchState.Notification, m.StateAt(Ms(10)));
    }

    [Fact]
    public void Timer_BeatsMusic()
    {
        var m = new NotchStateMachine();
        m.SetMusicPlaying(true);
        m.SetTimerRunning(true);
        Assert.Equal(NotchState.Timer, m.StateAt(T0));
    }

    [Fact]
    public void Opening_ShowsFor1600ms_BelowNotification()
    {
        var m = new NotchStateMachine();
        m.SetMusicPlaying(true);
        m.Opening("Calculator", T0);
        Assert.Equal(NotchState.Opening, m.StateAt(Ms(1599)));
        Assert.Equal("Calculator", m.OpeningName);
        Assert.Equal(NotchState.Music, m.StateAt(Ms(1600)));

        m.Notify(N1, Ms(2000));
        m.Opening("Notepad", Ms(2100)); // during a notification: dropped
        Assert.Equal(NotchState.Notification, m.StateAt(Ms(2200)));
        Assert.Equal(NotchState.Music, m.StateAt(Ms(5600)));
    }

    [Fact]
    public void Fullscreen_HidesEverything_AndDropsTransients()
    {
        var m = new NotchStateMachine();
        m.SetFullscreen(true);
        m.Notify(N1, T0);
        Assert.Equal(NotchState.Hidden, m.StateAt(Ms(10)));
        m.SetFullscreen(false);
        Assert.Equal(NotchState.Resting, m.StateAt(Ms(1000)));
    }

    [Fact]
    public void EnteringFullscreen_DropsShowingNotification()
    {
        var m = new NotchStateMachine();
        m.Notify(N1, T0);
        m.SetFullscreen(true);
        m.SetFullscreen(false);
        Assert.Equal(NotchState.Resting, m.StateAt(Ms(100)));
    }

    [Fact]
    public void Power_ShowsFor2400ms_ThenFallsBack()
    {
        var m = new NotchStateMachine();
        m.SetMusicPlaying(true);
        m.PowerChanged(charging: true, percent: 36, T0);
        Assert.Equal(NotchState.Power, m.StateAt(Ms(2399)));
        Assert.Equal((true, 36), m.CurrentPower);
        Assert.Equal(NotchState.Music, m.StateAt(Ms(2400)));
        Assert.Equal(TimeSpan.FromMilliseconds(2400), NotchStateMachine.PowerTime);
    }

    [Fact]
    public void Power_BelowNotification_AboveOpening()
    {
        var m = new NotchStateMachine();
        m.Opening("Calculator", T0);
        m.PowerChanged(false, 80, Ms(100));
        Assert.Equal(NotchState.Power, m.StateAt(Ms(200)));      // replaces the opening hint
        m.Notify(N1, Ms(300));
        Assert.Equal(NotchState.Notification, m.StateAt(Ms(400))); // a notification still wins
        m.PowerChanged(true, 81, Ms(500));                          // during a notification: dropped
        Assert.Equal(NotchState.Resting, m.StateAt(Ms(3900)));
    }

    [Fact]
    public void Power_IgnoredInFullscreen()
    {
        var m = new NotchStateMachine();
        m.SetFullscreen(true);
        m.PowerChanged(true, 50, T0);
        m.SetFullscreen(false);
        Assert.Equal(NotchState.Resting, m.StateAt(Ms(100)));
    }

    [Fact]
    public void Open_BeatsNotificationAndTimer()
    {
        var m = new NotchStateMachine();
        m.SetTimerRunning(true);
        m.Notify(N1, T0);
        m.SetOpen(true);
        Assert.Equal(NotchState.Open, m.StateAt(Ms(10)));
        m.SetOpen(false);
        Assert.Equal(NotchState.Notification, m.StateAt(Ms(20)));
    }

    [Fact]
    public void Open_HiddenInFullscreen()
    {
        var m = new NotchStateMachine();
        m.SetOpen(true);
        m.SetFullscreen(true);
        Assert.Equal(NotchState.Hidden, m.StateAt(T0));
        m.SetFullscreen(false);
        Assert.Equal(NotchState.Resting, m.StateAt(T0)); // entering full screen also closed it
    }

    [Fact]
    public void NotificationDuringOpen_ShowsAfterCloseIfStillDue()
    {
        var m = new NotchStateMachine();
        m.SetOpen(true);
        m.Notify(N1, T0);
        m.SetOpen(false);
        Assert.Equal(NotchState.Notification, m.StateAt(Ms(1000)));

        var m2 = new NotchStateMachine();
        m2.SetOpen(true);
        m2.Notify(N1, T0);
        m2.SetOpen(false);
        Assert.Equal(NotchState.Resting, m2.StateAt(Ms(4000))); // its 3.6 s ran out while open
    }

    [Fact]
    public void Suppressed_HidesTheNotch_ButKeepsAShowingNotification()
    {
        // A screen tool (Draw, Snip, Grab text, Color) must not have the notch over it or in its picture (user 2026-11-19);
        // unlike full screen, a notification still due shows again afterwards.
        var m = new NotchStateMachine();
        m.Notify(N1, T0);
        m.SetSuppressed(true);
        Assert.Equal(NotchState.Hidden, m.StateAt(Ms(100)));
        m.SetSuppressed(false);
        Assert.Equal(NotchState.Notification, m.StateAt(Ms(200)));
    }

    [Fact]
    public void NextChangeAt_IsTransientEnd()
    {
        var m = new NotchStateMachine();
        Assert.Null(m.NextChangeAt(T0));
        m.Notify(N1, T0);
        Assert.Equal(Ms(3600), m.NextChangeAt(Ms(100)));
        Assert.Null(m.NextChangeAt(Ms(3600)));
    }
}
