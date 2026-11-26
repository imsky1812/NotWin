using NotWin.Core.Widgets;
using static NotWin.Core.Widgets.WidgetKind;

namespace NotWin.Core.Tests.Widgets;

/// <summary>Plan 13: the Nothing widget sheet's kinds, sizes, round rule and per-widget choices.</summary>
public sealed class SheetRulesTests
{
    private static WidgetSize S(int w, int h) => new(w, h);
    private static WidgetPlacement P(WidgetKind k, string? choice = null, string? shape = null, int w = 1, int h = 1) =>
        new(k, S(w, h), 0, 0, "", choice is null && shape is null ? null : new WidgetConfig(Shape: shape, Choice: choice));

    public static readonly WidgetKind[] SheetKinds = [Toggle, AnalogClock, WorldClock, StackedClock, Stopwatch, ScreenTime, SystemMeters, SunCompass];

    [Fact]
    public void Rules_NewKindSizes()
    {
        Assert.Equal([S(1, 1), S(2, 1)], WidgetRules.Sizes(Toggle));
        Assert.Equal([S(1, 1), S(2, 2)], WidgetRules.Sizes(AnalogClock));
        Assert.Equal([S(1, 1), S(2, 1)], WidgetRules.Sizes(WorldClock));
        Assert.Equal([S(1, 2), S(1, 1)], WidgetRules.Sizes(StackedClock));
        Assert.Equal([S(1, 1), S(2, 1)], WidgetRules.Sizes(Stopwatch));
        Assert.Equal([S(2, 1), S(2, 2)], WidgetRules.Sizes(ScreenTime));
        Assert.Equal([S(2, 1), S(2, 2)], WidgetRules.Sizes(SystemMeters));
        Assert.Equal([S(1, 1), S(2, 2)], WidgetRules.Sizes(SunCompass));
        Assert.Equal(13, (int)Toggle);
        Assert.Equal(20, (int)SunCompass);
    }

    [Fact]
    public void Rules_Repeatable()
    {
        foreach (var k in new[] { Photo, Folder, Toggle, WorldClock }) Assert.True(WidgetRules.IsRepeatable(k), k.ToString());
        foreach (var k in new[] { AnalogClock, StackedClock, Stopwatch, ScreenTime, SystemMeters, SunCompass })
            Assert.False(WidgetRules.IsRepeatable(k), k.ToString());
    }

    [Fact]
    public void Rules_PickerOrder() => Assert.Equal(
        [Photo, Folder, Toggle, Clock, AnalogClock, StackedClock, WorldClock, Snake, WidgetKind.TicTacToe, WidgetKind.Timer, Stopwatch, Calendar, Agenda,
         Notes, Alarm, Weather, Media, Battery, ScreenTime, SystemMeters, SunCompass],
        WidgetRules.PickerOrder);

    [Fact]
    public void IsRound()
    {
        foreach (var k in new[] { Toggle, AnalogClock, WorldClock, StackedClock, Stopwatch, SunCompass })
            foreach (var size in WidgetRules.Sizes(k))
                Assert.True(WidgetRules.IsRound(new WidgetPlacement(k, size, 0, 0)), $"{k} {size}");
        Assert.True(WidgetRules.IsRound(P(Photo, shape: "circle", w: 2, h: 2)));
        Assert.False(WidgetRules.IsRound(P(Photo, shape: "square", w: 2, h: 2)));
        Assert.False(WidgetRules.IsRound(P(ScreenTime, w: 2)));
        Assert.False(WidgetRules.IsRound(P(Clock, w: 3)));
    }

    [Fact]
    public void Default_Toggle_FirstUnused()
    {
        Assert.Equal("dark", WidgetChoices.Default(Toggle, [P(Toggle, "wifi"), P(Toggle, "bluetooth")]));
        Assert.Equal("wifi", WidgetChoices.Default(Toggle, WidgetChoices.Toggles.Select(t => P(Toggle, t))));
        Assert.Equal("UTC", WidgetChoices.Default(WorldClock, []));
        Assert.Equal("light", WidgetChoices.Default(AnalogClock, []));
        Assert.Null(WidgetChoices.Default(Clock, []));
    }

    [Fact]
    public void Normalize()
    {
        Assert.Equal("wifi", WidgetChoices.Normalize(Toggle, "bogus"));
        Assert.Equal("UTC", WidgetChoices.Normalize(WorldClock, "Mars Standard Time"));
        Assert.Equal("light", WidgetChoices.Normalize(AnalogClock, null));
        Assert.Equal("mic", WidgetChoices.Normalize(Toggle, "mic"));
        Assert.Equal("India Standard Time", WidgetChoices.Normalize(WorldClock, "India Standard Time"));
        Assert.Equal("x", WidgetChoices.Normalize(Photo, "x"));
    }

    [Fact]
    public void Zones_AreTheFixedList()
    {
        Assert.Equal(26, WidgetChoices.Zones.Count);
        Assert.Contains("UTC", WidgetChoices.Zones);
        Assert.Equal(WidgetChoices.Zones.Count, WidgetChoices.Zones.Distinct().Count());
    }

    [Fact]
    public void Sanitize_FixesUnknownChoices()
    {
        var s = DesktopLayout.Sanitize(new DesktopLayout([P(Toggle, "zzz"), new WidgetPlacement(WorldClock, S(1, 1), 148, 0)]), 1600, 900);
        Assert.Equal("wifi", s.Widgets[0].Config?.Choice);
        Assert.Equal("UTC", s.Widgets[1].Config?.Choice);
    }

    [Fact]
    public void Sanitize_LeavesOtherKindsConfigAlone()
    {
        var s = DesktopLayout.Sanitize(new DesktopLayout([new WidgetPlacement(Clock, S(3, 1), 0, 0)]), 1600, 900);
        Assert.Null(s.Widgets[0].Config);
    }

    [Fact]
    public void Sanitize_KeepsTallSize()
    {
        var s = DesktopLayout.Sanitize(new DesktopLayout([new WidgetPlacement(StackedClock, S(1, 2), 0, 0)]), 1600, 900);
        Assert.Equal(S(1, 2), s.Widgets[0].Size);
        Assert.Equal(284, s.Widgets[0].Size.PxH);
    }

    [Fact]
    public void Add_Toggle_TakesDefaultChoice()
    {
        var (after, id) = new DesktopLayout([]).Add(Toggle, 1600, 900, () => "t", WidgetChoices.ConfigFor(Toggle, []));
        Assert.Equal("wifi", after.Widgets[after.IndexOf(id)].Config?.Choice);
        Assert.Null(WidgetChoices.ConfigFor(Clock, []));
    }
}
