using NothingShell.Core;
using NothingShell.Dock;

namespace NothingShell.Tests;

public sealed class DotDrainControllerTests
{
    private sealed class Env : IDrainEnvironment
    {
        public bool Enabled { get; set; } = true;
        public bool Suppressed { get; set; }
        public RectI Monitor { get; set; } = new(0, 0, 1920, 1080);
        public RectI? Icon { get; set; } = new(900, 1020, 940, 1060);
        public RectI? Window { get; set; } = new(100, 100, 100 + 140, 100 + 70);
        public RectI? IconRect(nint hwnd) => Icon;
        public RectI? DrainRect(nint hwnd, bool restoring) => Window;
    }

    private sealed class Overlay : IDrainOverlay
    {
        public object? Picture = new();
        public readonly List<(object? Picture, RectI Window, double Tx, double Ty, bool Restore, Action Done)> Plays = [];
        public int Closed;
        public void Snapshot(nint hwnd, RectI screen, Action<object?> done) => done(Picture);
        public void Play(object? picture, RectI window, RectI icon, bool restore, Action done) =>
            Plays.Add((picture, window, (icon.Left + icon.Right) / 2.0, (icon.Top + icon.Bottom) / 2.0, restore, done));
        public void Close() => Closed++;
    }

    private static (DotDrainController C, Env E, Overlay O) Make()
    {
        var env = new Env();
        var overlay = new Overlay();
        return (new DotDrainController(env, () => overlay), env, overlay);
    }

    [Fact]
    public void Controller_PlaysTheWindowsPictureTowardTheIcon()
    {
        var (c, e, o) = Make();
        c.OnMinimize(1, start: true);
        var play = Assert.Single(o.Plays);
        Assert.Same(o.Picture, play.Picture);
        Assert.Equal(e.Window, play.Window);
        Assert.Equal((920.0, 1040.0), (play.Tx, play.Ty));
        Assert.False(play.Restore);
    }

    // No picture (DRM video, a game, a window closing): the genie still plays, as a plain black card.
    [Fact]
    public void Controller_NoPicture_StillPlays()
    {
        var (c, _, o) = Make();
        o.Picture = null;
        c.OnMinimize(1, true);
        Assert.Null(Assert.Single(o.Plays).Picture);
    }

    [Fact]
    public void Controller_OtherMonitor_Skips()
    {
        var (c, e, o) = Make();
        e.Window = new RectI(2500, 100, 2700, 300);
        c.OnMinimize(1, true);
        Assert.Empty(o.Plays);
    }

    [Fact]
    public void Controller_NotInTheDock_Skips()
    {
        var (c, e, o) = Make();
        e.Icon = null;
        c.OnMinimize(1, true);
        Assert.Empty(o.Plays);
    }

    [Fact]
    public void Controller_Suppressed_Skips()
    {
        var (c, e, o) = Make();
        e.Suppressed = true;
        c.OnMinimize(1, true);
        Assert.Empty(o.Plays);
    }

    [Fact]
    public void Controller_Disabled_Skips()
    {
        var (c, e, o) = Make();
        e.Enabled = false;
        c.OnMinimize(1, true);
        Assert.Empty(o.Plays);
    }

    [Fact]
    public void Controller_FifthAtOnce_Skips()
    {
        var (c, _, o) = Make();
        for (var i = 1; i <= 5; i++) c.OnMinimize(i, true);
        Assert.Equal(4, o.Plays.Count);
    }

    [Fact]
    public void Controller_OverlayGoneAfterLastEnds()
    {
        var (c, _, o) = Make();
        c.OnMinimize(1, true);
        c.OnMinimize(2, true);
        o.Plays[0].Done();
        Assert.Equal(0, o.Closed);
        o.Plays[1].Done();
        Assert.Equal(1, o.Closed);
        c.OnMinimize(3, true);
        Assert.Equal(3, o.Plays.Count);
    }

    // Option A (2026-11-21): restoring is not animated — Windows has already put the window back, so a picture growing
    // over it would look doubled.
    [Fact]
    public void Controller_Restore_IsNotAnimated()
    {
        var (c, _, o) = Make();
        c.OnMinimize(1, start: false);
        Assert.Empty(o.Plays);
    }

    // Review (critical): with the dock auto-hidden, the window is still in the dock — aim at where it appears.
    [Fact]
    public void Environment_HiddenDock_AimsAtTheBottomCentre()
    {
        var env = new DockDrainEnvironment(_ => new IconLookup(InDock: true, Rect: null), () => true, () => false);
        var icon = env.IconRect(1);
        Assert.NotNull(icon);
        var mon = env.Monitor;
        Assert.Equal((mon.Left + mon.Right) / 2, icon.Value.Left);
        Assert.True(icon.Value.Top < mon.Bottom && icon.Value.Top > mon.Bottom - 200);
        var notInDock = new DockDrainEnvironment(_ => new IconLookup(false, null), () => true, () => false);
        Assert.Null(notInDock.IconRect(1));
    }

    private sealed class ThrowingOverlay : IDrainOverlay
    {
        public int Plays;
        public void Snapshot(nint hwnd, RectI screen, Action<object?> done) => done(null);
        public void Play(object? picture, RectI window, RectI icon, bool restore, Action done) { Plays++; throw new InvalidOperationException("boom"); }
        public void Close() { }
    }

    // Review (important): a failure after the snapshot must not escape and must free the slot.
    [Fact]
    public void Controller_PlayThrows_IsContainedAndFreesTheSlot()
    {
        var overlay = new ThrowingOverlay();
        var c = new DotDrainController(new Env(), () => overlay);
        for (var i = 1; i <= 6; i++) c.OnMinimize(i, true);
        Assert.Equal(6, overlay.Plays);
    }
}
