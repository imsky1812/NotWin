using NotWin.Core.Widgets;

namespace NotWin.Core.Tests.Widgets;

/// <summary>
/// User 2026-11-27 (screenshot of the ＋ Add list): Clock, World clock, Timer, Stopwatch, Alarm and Screen time all
/// showed the same cup glyph. Every widget kind now has its own dot icon.
/// </summary>
public sealed class WidgetIconsTests
{
    private static readonly WidgetKind[] AllKinds = Enum.GetValues<WidgetKind>();

    [Fact]
    public void EveryKind_MapsToARealGlyph()
    {
        foreach (var kind in AllKinds)
            Assert.True(DotArt.Has(WidgetIcons.For(kind)), $"{kind} -> '{WidgetIcons.For(kind)}' is not a known dot glyph");
    }

    [Fact]
    public void TheClockFamily_AllDiffer()
    {
        // The six the user flagged, plus the two other clock faces: each reads as its own thing, not a shared cup.
        WidgetKind[] family =
        [
            WidgetKind.Clock, WidgetKind.AnalogClock, WidgetKind.WorldClock, WidgetKind.StackedClock,
            WidgetKind.Timer, WidgetKind.Stopwatch, WidgetKind.Alarm, WidgetKind.ScreenTime,
        ];
        var icons = family.Select(WidgetIcons.For).ToList();
        Assert.Equal(icons.Count, icons.Distinct().Count());
        Assert.DoesNotContain("cup", icons);
    }

    [Fact]
    public void Toggle_And_SystemMeters_Differ() =>
        Assert.NotEqual(WidgetIcons.For(WidgetKind.Toggle), WidgetIcons.For(WidgetKind.SystemMeters));

    [Fact]
    public void EveryGlyph_Is11By11_DotsAndHashesOnly()
    {
        foreach (var name in DotArt.Names)
        {
            var rows = DotArt.Rows(name);
            Assert.Equal(11, rows.Length);
            foreach (var row in rows)
            {
                Assert.Equal(11, row.Length);
                Assert.True(row.All(c => c is '#' or '.'), $"'{name}' has a bad character in \"{row}\"");
            }
        }
    }
}
