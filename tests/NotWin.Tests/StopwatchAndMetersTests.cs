using System.Windows.Controls;
using NotWin.Core.Widgets;
using NotWin.Notch;
using NotWin.Widgets;
using static NotWin.Tests.ClockWidgetsTests;
using static NotWin.Tests.WidgetSurfaceTests;

namespace NotWin.Tests;

/// <summary>Plan 13: stopwatch and system meters.</summary>
public sealed class StopwatchAndMetersTests
{
    private TimeSpan _now = TimeSpan.FromMinutes(1);

    private StopwatchWidget Stopwatch() => new(() => _now);

    [Fact]
    public void Stopwatch_Click_StartsAndPauses() => OnSta(() =>
    {
        var w = Stopwatch();
        w.Build(new WidgetSize(1, 1));
        w.Click();
        Assert.True(w.Running);
        Assert.True(w.Ticking);
        _now += TimeSpan.FromSeconds(3);
        w.Click();
        Assert.False(w.Running);
        Assert.False(w.Ticking);
        w.Dispose();
    });

    [Fact]
    public void Stopwatch_DoubleClickWhilePaused_Resets() => OnSta(() =>
    {
        var w = Stopwatch();
        w.Build(new WidgetSize(2, 1));
        w.Click();
        _now += TimeSpan.FromSeconds(4);
        w.Click();                 // paused at 4 s
        w.Click();                 // first click of a double-click starts it…
        _now += TimeSpan.FromMilliseconds(200);
        w.DoubleClick();           // …the second one resets
        Assert.False(w.Running);
        Assert.Equal(TimeSpan.Zero, w.Elapsed);
        w.Dispose();
    });

    [Fact]
    public void Stopwatch_DoubleClickWhileRunning_OnlyPauses() => OnSta(() =>
    {
        var w = Stopwatch();
        w.Build(new WidgetSize(1, 1));
        w.Click();
        _now += TimeSpan.FromSeconds(5);
        w.Click();
        w.DoubleClick();
        Assert.False(w.Running);
        Assert.Equal(TimeSpan.FromSeconds(5), w.Elapsed);
        w.Dispose();
    });

    [Fact]
    public void Stopwatch_RingLitEqualsSecondsOfMinute() => OnSta(() =>
    {
        var w = Stopwatch();
        var view = Laid(w.Build(new WidgetSize(1, 1)), new WidgetSize(1, 1));
        w.Click();
        _now += TimeSpan.FromSeconds(75);
        w.Click();
        var tree = Tree(view).ToList();
        Assert.Equal(15, tree.OfType<DotRingView>().Single().Lit);
        Assert.Contains(tree.OfType<DotMatrix>(), d => d.Text == "01:15");
        w.Dispose();
    });

    [Fact]
    public void Stopwatch_Paused_NoTimer() => OnSta(() =>
    {
        var w = Stopwatch();
        w.Build(new WidgetSize(1, 1));
        w.Click();
        w.SetPaused(true);
        Assert.False(w.Ticking);
        Assert.True(w.Running);
        w.SetPaused(false);
        Assert.True(w.Ticking);
        w.Dispose();
    });

    private static readonly TimeSpan T0 = TimeSpan.FromHours(1);

    private sealed class FakeSource
    {
        public Queue<MeterReading> Readings = new();
        public MeterReading? Next() => Readings.Count == 0 ? null : Readings.Dequeue();
    }

    private static MeterReading R(ulong idle, ulong kernel, ulong user, int ram, long down, long up, double s) =>
        new((idle, kernel, user), ram, down, up, T0 + TimeSpan.FromSeconds(s));

    [Fact]
    public void Meters_FirstSampleShowsNothingHot() => OnSta(() =>
    {
        var src = new FakeSource();
        src.Readings.Enqueue(R(0, 0, 0, 97, 0, 0, 0));
        using var sampler = new MeterSampler(src.Next);
        (int Cpu, int Ram, double Down, double Up)? last = null;
        sampler.Sampled += (c, r, d, u) => last = (c, r, d, u);
        sampler.Sample();
        Assert.Equal((0, 97, 0.0, 0.0), last);
    });

    [Fact]
    public void Meters_RatesAndCpuFromDeltas() => OnSta(() =>
    {
        var src = new FakeSource();
        src.Readings.Enqueue(R(1000, 2000, 3000, 40, 1_000, 500, 0));
        src.Readings.Enqueue(R(1050, 2060, 3040, 41, 2_001_000, 4_500, 2));
        using var sampler = new MeterSampler(src.Next);
        (int Cpu, int Ram, double Down, double Up)? last = null;
        sampler.Sampled += (c, r, d, u) => last = (c, r, d, u);
        sampler.Sample();
        sampler.Sample();
        Assert.Equal((50, 41, 1e6, 2000.0), last);
    });

    [Fact]
    public void Meters_History_KeepsTwenty() => OnSta(() =>
    {
        var src = new FakeSource();
        for (var i = 0; i < 25; i++) src.Readings.Enqueue(R((ulong)i * 10, (ulong)i * 20, (ulong)i * 20, 10, 0, 0, i * 2));
        using var sampler = new MeterSampler(src.Next);
        for (var i = 0; i < 25; i++) sampler.Sample();
        Assert.Equal(20, sampler.CpuHistory.Count);
    });

    // Read-only: GetSystemTimes / GlobalMemoryStatusEx / interface counters on this PC.
    [Fact]
    public void Meters_ReadSystem_GivesAReading()
    {
        var r = MeterSampler.ReadSystem();
        Assert.NotNull(r);
        Assert.InRange(r.RamPercent, 1, 100);
        Assert.True(r.Cpu.Kernel >= r.Cpu.Idle);
    }

    [Fact]
    public void Meters_StopWhenPaused() => OnSta(() =>
    {
        var src = new FakeSource();
        var w = new SystemMetersWidget(() => new MeterSampler(src.Next));
        w.Build(new WidgetSize(2, 1));
        Assert.True(w.Sampling);
        w.SetPaused(true);
        Assert.False(w.Sampling);
        w.SetPaused(false);
        Assert.True(w.Sampling);
        w.Dispose();
        Assert.False(w.Sampling);
    });

    [Fact]
    public void Meters_NoAdapterNames_HotBarIsRed() => OnSta(() =>
    {
        var src = new FakeSource();
        src.Readings.Enqueue(R(0, 0, 0, 95, 0, 0, 0));
        src.Readings.Enqueue(R(0, 100, 100, 95, 3_000_000, 0, 2));
        var w = new SystemMetersWidget(() => new MeterSampler(src.Next));
        foreach (var size in WidgetRules.Sizes(WidgetKind.SystemMeters))
        {
            var view = Laid(w.Build(size), size);
            w.SampleNow();
            w.SampleNow();
            var texts = Tree(view).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.All(texts, t => Assert.Matches(@"^(CPU|RAM|NET|DOWN|UP|[0-9.]+ ?(%|KB/s|MB/s))$", t));
            Assert.Contains(Tree(view).OfType<DotBar>(), b => b.On == NotWin.Notch.Dashboard.TileChrome.Red);
        }
        w.Dispose();
    });
}
