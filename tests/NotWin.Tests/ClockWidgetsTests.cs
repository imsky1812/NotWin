using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NotWin.Core.Widgets;
using NotWin.Notch;
using NotWin.Widgets;
using static NotWin.Tests.WidgetSurfaceTests;

namespace NotWin.Tests;

/// <summary>Plan 13: analogue, world and stacked clocks.</summary>
public sealed class ClockWidgetsTests
{
    internal static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var d in Tree(VisualTreeHelper.GetChild(root, i))) yield return d;
    }

    internal static FrameworkElement Laid(FrameworkElement e, WidgetSize size)
    {
        e.Measure(new Size(size.PxW, size.PxH));
        e.Arrange(new Rect(0, 0, size.PxW, size.PxH));
        return e;
    }

    private static readonly DateTime Utc10 = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Analog_LightFace_HasSecondsHand_DarkFaceDoesNot() => OnSta(() =>
    {
        var w = new AnalogClockWidget(() => new DateTime(2026, 1, 15, 3, 15, 0));
        w.Configure(new WidgetConfig(Choice: "light"));
        w.Build(new WidgetSize(1, 1));
        Assert.True(w.ShowsSeconds);
        Assert.Equal(Surface.Light, w.Surface);
        w.Configure(new WidgetConfig(Choice: "dark"));
        w.Build(new WidgetSize(2, 2));
        Assert.False(w.ShowsSeconds);
        Assert.Equal(Surface.Dark, w.Surface);
        w.Dispose();
    });

    [Fact]
    public void Analog_Paused_StopsTimer() => OnSta(() =>
    {
        var w = new AnalogClockWidget(() => DateTime.Now);
        w.Configure(new WidgetConfig(Choice: "light"));
        w.Build(new WidgetSize(1, 1));
        Assert.True(w.Ticking);
        w.SetPaused(true);
        Assert.False(w.Ticking);
        w.SetPaused(false);
        Assert.True(w.Ticking);
        w.Dispose();
        Assert.False(w.Ticking);
    });

    [Fact]
    public void World_ShowsZoneTimeAndLabel() => OnSta(() =>
    {
        var w = new WorldClockWidget(() => Utc10);
        w.Configure(new WidgetConfig(Choice: "India Standard Time"));
        foreach (var size in new[] { new WidgetSize(1, 1), new WidgetSize(2, 1) })
        {
            var tree = Tree(Laid(w.Build(size), size)).ToList();
            Assert.Contains(tree.OfType<DotMatrix>(), d => d.Text == "15:30");
            Assert.Contains(tree.OfType<TextBlock>(), t => t.Text.Contains("GMT+5:30"));
            Assert.Contains(tree.OfType<TextBlock>(), t => t.Text.Contains("THU"));
        }
        w.Dispose();
    });

    [Fact]
    public void World_2x1_ShowsDifferenceFromLocal() => OnSta(() =>
    {
        var w = new WorldClockWidget(() => Utc10, TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"));
        w.Configure(new WidgetConfig(Choice: "India Standard Time"));
        var tree = Tree(Laid(w.Build(new WidgetSize(2, 1)), new WidgetSize(2, 1))).ToList();
        Assert.Contains(tree.OfType<TextBlock>(), t => t.Text == "+4H 30M");
        w.Dispose();
    });

    [Fact]
    public void Stacked_SplitsHoursAndMinutes() => OnSta(() =>
    {
        var w = new StackedClockWidget(() => new DateTime(2026, 1, 15, 22, 10, 5));
        foreach (var size in WidgetRules.Sizes(WidgetKind.StackedClock))
        {
            var dots = Tree(Laid(w.Build(size), size)).OfType<DotMatrix>().Select(d => d.Text).ToList();
            Assert.Equal(["22", "10"], dots);
        }
        w.Dispose();
    });

    [Fact]
    public void All_BuildEverySize() => OnSta(() =>
    {
        IDesktopWidget[] widgets =
        [
            new AnalogClockWidget(() => DateTime.Now), new WorldClockWidget(() => DateTime.UtcNow), new StackedClockWidget(() => DateTime.Now),
        ];
        foreach (var w in widgets)
        {
            w.Configure(null);
            foreach (var size in WidgetRules.Sizes(w.Kind)) Laid(w.Build(size), size);
            w.Dispose();
        }
    });
}
