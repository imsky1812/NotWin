using NothingShell.Core;
using NothingShell.Core.Dock;
using NothingShell.Dock;

namespace NothingShell.Tests;

public sealed class DotDrainControllerTests
{
    private sealed class Env : IDrainEnvironment
    {
        public bool Enabled { get; set; } = true;
        public bool Suppressed { get; set; }
        public RectI Monitor { get; set; } = new(0, 0, 1920, 1080);
        public double Pitch { get; set; } = 14;
        public RectI? Icon { get; set; } = new(900, 1020, 940, 1060);
        public RectI? Normal { get; set; } = new(100, 100, 100 + 140, 100 + 70);
        public RectI? IconRect(nint hwnd) => Icon;
        public RectI? NormalRect(nint hwnd) => Normal;
    }

    private sealed class Overlay : IDrainOverlay
    {
        public byte[]? Picture = Enumerable.Repeat((byte)255, 10 * 5).ToArray();
        public readonly List<(IReadOnlyList<DrainDot> Dots, double Tx, double Ty, bool Restore, Action Done)> Plays = [];
        public int Closed;
        public void Snapshot(nint hwnd, RectI screen, int stepPx, Action<byte[]?> done) => done(Picture);
        public void Play(IReadOnlyList<DrainDot> dots, double tx, double ty, bool restore, Action done) => Plays.Add((dots, tx, ty, restore, done));
        public void Close() => Closed++;
    }

    private static (DotDrainController C, Env E, Overlay O) Make()
    {
        var env = new Env();
        var overlay = new Overlay();
        return (new DotDrainController(env, () => overlay), env, overlay);
    }

    [Fact]
    public void Controller_PlaysTowardTheIcon()
    {
        var (c, _, o) = Make();
        c.OnMinimize(1, start: true);
        var play = Assert.Single(o.Plays);
        Assert.Equal((920.0, 1040.0), (play.Tx, play.Ty)); // the icon's centre
        Assert.False(play.Restore);
        Assert.Equal(50, play.Dots.Count);                 // 10 × 5 bright cells
        Assert.Equal(14 * 0.42, play.Dots[0].R, 6);
    }

    [Fact]
    public void Controller_ThumbnailFails_UsesFallback()
    {
        var (c, _, o) = Make();
        o.Picture = null;
        c.OnMinimize(1, true);
        Assert.All(Assert.Single(o.Plays).Dots, d => Assert.Equal(14 * 0.30, d.R, 6));
    }

    [Fact]
    public void Controller_NearBlack_UsesFallback()
    {
        var (c, _, o) = Make();
        o.Picture = new byte[50];
        c.OnMinimize(1, true);
        var dots = Assert.Single(o.Plays).Dots;
        Assert.Equal(50, dots.Count);
        Assert.All(dots, d => Assert.Equal(14 * 0.30, d.R, 6));
    }

    [Fact]
    public void Controller_OtherMonitor_Skips()
    {
        var (c, e, o) = Make();
        e.Normal = new RectI(2500, 100, 2700, 300); // on a second monitor to the right
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
        c.OnMinimize(3, true); // a new overlay for the next one
        Assert.Equal(3, o.Plays.Count);
    }

    [Fact]
    public void Controller_Restore_PlaysReverse()
    {
        var (c, _, o) = Make();
        c.OnMinimize(1, start: false);
        Assert.True(Assert.Single(o.Plays).Restore);
    }
}
