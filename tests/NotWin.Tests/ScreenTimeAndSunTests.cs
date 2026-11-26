using System.IO;
using System.Windows.Controls;
using NotWin.Core.Forecast;
using NotWin.Core.Widgets;
using NotWin.Notch;
using NotWin.Widgets;
using static NotWin.Tests.ClockWidgetsTests;
using static NotWin.Tests.WidgetSurfaceTests;

namespace NotWin.Tests;

/// <summary>Plan 13: screen time and the sun compass.</summary>
public sealed class ScreenTimeAndSunTests
{
    private DateTime _now = new(2026, 11, 26, 10, 0, 0);
    private TimeSpan _idle = TimeSpan.FromSeconds(5);

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "notwin-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private ScreenTimeTracker Tracker(string dir) => new(dir, () => _now, () => _idle);

    [Fact]
    public void Tracker_WritesAndReloads() => OnSta(() =>
    {
        var dir = TempDir();
        try
        {
            var t = Tracker(dir);
            t.Tick();
            _now = _now.AddMinutes(1); t.Tick();
            _now = _now.AddMinutes(1); t.Tick();
            Assert.Equal(2, t.Log.Today(_now));
            t.Flush();
            t.Dispose();
            Assert.True(File.Exists(Path.Combine(dir, "screentime.json")));
            using var again = Tracker(dir);
            Assert.Equal(2, again.Log.Today(_now));
        }
        finally { Directory.Delete(dir, true); }
    });

    [Fact]
    public void Tracker_LockedSamplesIgnored() => OnSta(() =>
    {
        var dir = TempDir();
        try
        {
            using var t = Tracker(dir);
            t.Tick();
            t.OnSessionSwitch(locked: true);
            _now = _now.AddMinutes(1); t.Tick();
            _now = _now.AddMinutes(1); t.Tick();
            Assert.Equal(0, t.Log.Today(_now));
            t.OnSessionSwitch(locked: false);
            _now = _now.AddMinutes(1); t.Tick();
            Assert.Equal(1, t.Log.Today(_now));
        }
        finally { Directory.Delete(dir, true); }
    });

    [Fact]
    public void Tracker_RaisesChanged_WritesAtMostEvery5Min() => OnSta(() =>
    {
        var dir = TempDir();
        try
        {
            using var t = Tracker(dir);
            var changed = 0;
            t.Changed += () => changed++;
            t.Tick();
            for (var i = 0; i < 3; i++) { _now = _now.AddMinutes(1); t.Tick(); }
            Assert.Equal(3, changed);
            Assert.False(File.Exists(Path.Combine(dir, "screentime.json")));
            for (var i = 0; i < 3; i++) { _now = _now.AddMinutes(1); t.Tick(); }
            Assert.True(File.Exists(Path.Combine(dir, "screentime.json")));
        }
        finally { Directory.Delete(dir, true); }
    });

    [Fact]
    public void ScreenTime_2x2_HasSevenColumns_TodayRed() => OnSta(() =>
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "screentime.json"), """{ "2026-11-25": 300, "2026-11-26": 380 }""");
            using var t = Tracker(dir);
            var w = new ScreenTimeWidget(t);
            var tree = Tree(Laid(w.Build(new WidgetSize(2, 2)), new WidgetSize(2, 2))).ToList();
            var chart = tree.OfType<DotChartView>().Single();
            Assert.Equal(7, chart.ColumnCount);
            Assert.Equal(6, chart.AccentColumn);
            Assert.Equal([0, 0, 0, 0, 0, 5, 6], chart.Columns);
            Assert.Contains(tree.OfType<DotMatrix>(), d => d.Text == "6H20");
            var small = Tree(Laid(w.Build(new WidgetSize(2, 1)), new WidgetSize(2, 1))).ToList();
            Assert.Contains(small.OfType<TextBlock>(), b => b.Text == "AVG 5H00");
            w.Dispose();
        }
        finally { Directory.Delete(dir, true); }
    });

    private static readonly WeatherPlace London = new("Secretville", "GB", 51.5074, -0.1278);

    private static SunCompassWidget Sun(WeatherPlace? place, DateTime utc) => new(() => place, _ => { }, _ => { }, () => utc);

    [Fact]
    public void Sun_NoPlace_ShowsHint() => OnSta(() =>
    {
        var w = Sun(null, new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc));
        foreach (var size in WidgetRules.Sizes(WidgetKind.SunCompass))
        {
            var texts = Tree(Laid(w.Build(size), size)).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("SET A CITY IN SETTINGS", texts);
        }
        w.Dispose();
    });

    [Fact]
    public void Sun_NeverShowsTheCity() => OnSta(() =>
    {
        var w = Sun(London, new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc));
        foreach (var size in WidgetRules.Sizes(WidgetKind.SunCompass))
            Assert.DoesNotContain(Tree(Laid(w.Build(size), size)).OfType<TextBlock>(), t => t.Text.Contains("Secretville", StringComparison.OrdinalIgnoreCase));
        w.Dispose();
    });

    [Fact]
    public void Sun_ArrowFollowsAzimuth() => OnSta(() =>
    {
        var utc = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var w = Sun(London, utc);
        var view = Laid(w.Build(new WidgetSize(2, 2)), new WidgetSize(2, 2));
        Assert.InRange(w.ArrowAngle - SolarPosition.Position(utc, London.Lat, London.Lon).Azimuth, -1, 1);
        var texts = Tree(view).OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t.StartsWith("SUNRISE ", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.StartsWith("SUN 6", StringComparison.Ordinal)); // ≈ 62°
        w.Dispose();
    });

    [Fact]
    public void Sun_BelowHorizon_Dim_2x2Text() => OnSta(() =>
    {
        var w = Sun(London, new DateTime(2026, 12, 21, 0, 0, 0, DateTimeKind.Utc));
        var view = Laid(w.Build(new WidgetSize(2, 2)), new WidgetSize(2, 2));
        Assert.True(w.SunDown);
        Assert.Contains(Tree(view).OfType<TextBlock>(), t => t.Text == "BELOW HORIZON");
        w.Dispose();
    });

    [Fact]
    public void Sun_Paused_StopsTimer() => OnSta(() =>
    {
        var w = Sun(London, DateTime.UtcNow);
        w.Build(new WidgetSize(1, 1));
        Assert.True(w.Ticking);
        w.SetPaused(true);
        Assert.False(w.Ticking);
        w.Dispose();
    });
}
