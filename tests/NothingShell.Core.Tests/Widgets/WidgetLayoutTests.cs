using NothingShell.Core.Widgets;
using static NothingShell.Core.Widgets.WidgetKind;

namespace NothingShell.Core.Tests.Widgets;

public sealed class WidgetLayoutTests
{
    private const int AreaW = 1200, AreaH = 700;
    private static WidgetPlacement P(WidgetKind k, int w, int h, int x, int y) => new(k, new WidgetSize(w, h), x, y);
    private static int IndexOf(DesktopLayout l, WidgetKind k) => l.Widgets.ToList().FindIndex(p => p.Kind == k);

    [Fact]
    public void Default_MatchesMockup()
    {
        Assert.Equal(
            [P(Clock, 3, 1, 0, 0), P(Snake, 2, 2, 0, 148), P(WidgetKind.Timer, 2, 2, 296, 148), P(Calendar, 2, 2, 592, 148),
             P(Weather, 2, 1, 888, 148), P(Media, 2, 1, 888, 296), P(Battery, 1, 1, 0, 444)],
            DesktopLayout.Default.Widgets);
    }

    [Fact]
    public void Px_Formula()
    {
        Assert.Equal(136, WidgetSize.Px(1));
        Assert.Equal(284, WidgetSize.Px(2));
        Assert.Equal(432, WidgetSize.Px(3));
        Assert.Equal(580, WidgetSize.Px(4));
        Assert.Equal(432, new WidgetSize(3, 1).PxW);
        Assert.Equal(136, new WidgetSize(3, 1).PxH);
    }

    [Fact]
    public void SnapTo_HalfCell()
    {
        Assert.Equal(0, DesktopLayout.SnapTo(36));
        Assert.Equal(74, DesktopLayout.SnapTo(38));
        Assert.Equal(296, DesktopLayout.SnapTo(300));
    }

    [Fact]
    public void TryMove_SnapsAndMoves()
    {
        var l = DesktopLayout.Default;
        var (after, moved) = l.TryMove(IndexOf(l, Battery), 520, 470, AreaW, AreaH);
        Assert.True(moved);
        Assert.Equal(P(Battery, 1, 1, 518, 444), after.Widgets[IndexOf(l, Battery)]);
    }

    [Fact]
    public void TryMove_RefusesOverlap_StaysPut()
    {
        var l = DesktopLayout.Default;
        var (after, moved) = l.TryMove(IndexOf(l, Snake), 296, 148, AreaW, AreaH);
        Assert.False(moved);
        Assert.Same(l, after);
    }

    [Fact]
    public void TryMove_ClampsInsideTheArea()
    {
        var l = DesktopLayout.Default;
        var i = IndexOf(l, Battery);
        var (after, moved) = l.TryMove(i, 5000, 5000, AreaW, AreaH);
        Assert.True(moved);
        var p = after.Widgets[i];
        Assert.True(p.X + p.Size.PxW <= AreaW);
        Assert.True(p.Y + p.Size.PxH <= AreaH);
    }

    [Fact]
    public void CycleSize_AllowsOverlap_ThenFlagged()
    {
        var l = DesktopLayout.Default;
        var t = IndexOf(l, WidgetKind.Timer);
        l = l.CycleSize(t);
        Assert.Equal(new WidgetSize(1, 1), l.Widgets[t].Size);
        l = l.CycleSize(t);
        Assert.Equal(new WidgetSize(2, 1), l.Widgets[t].Size);

        var w = IndexOf(l, Weather);
        Assert.False(l.Overlaps(w));
        l = l.CycleSize(w).CycleSize(w); // 2x1 -> 1x1 -> 2x2: now reaches down over Media
        Assert.Equal(new WidgetSize(2, 2), l.Widgets[w].Size);
        Assert.True(l.Overlaps(w));
    }

    [Fact]
    public void Sanitize_ClampsOntoTheArea()
    {
        var s = DesktopLayout.Sanitize(new DesktopLayout([P(Clock, 3, 1, 5000, 3000)]), AreaW, AreaH);
        var c = s.Widgets.Single(p => p.Kind == Clock);
        Assert.True(c.X >= 0 && c.X + c.Size.PxW <= AreaW);
        Assert.True(c.Y >= 0 && c.Y + c.Size.PxH <= AreaH);
    }

    [Fact]
    public void Sanitize_KeepsOverlapsFlagged()
    {
        var s = DesktopLayout.Sanitize(new DesktopLayout([P(Snake, 2, 2, 0, 0), P(WidgetKind.Timer, 2, 2, 74, 74)]), AreaW, AreaH);
        var snake = s.Widgets.ToList().FindIndex(p => p.Kind == Snake);
        Assert.Equal(P(Snake, 2, 2, 0, 0), s.Widgets[snake]);
        Assert.Equal(P(WidgetKind.Timer, 2, 2, 74, 74), s.Widgets.Single(p => p.Kind == WidgetKind.Timer));
        Assert.True(s.Overlaps(snake));
    }

    [Fact]
    public void Sanitize_DropsUnknownAndDuplicates_AddsMissing()
    {
        var s = DesktopLayout.Sanitize(new DesktopLayout(
            [P((WidgetKind)(-1), 1, 1, 0, 0), P(Clock, 3, 1, 0, 0), P(Clock, 2, 1, 600, 0), P(Battery, 5, 5, 0, 444)]), AreaW, AreaH);
        Assert.Equal(Enum.GetValues<WidgetKind>().Length, s.Widgets.Count);           // every kind once
        Assert.Equal(P(Clock, 3, 1, 0, 0), s.Widgets.Single(p => p.Kind == Clock));    // first copy kept
        Assert.Equal(new WidgetSize(1, 1), s.Widgets.Single(p => p.Kind == Battery).Size); // bad size -> default
        for (var i = 0; i < s.Widgets.Count; i++)
            if (s.Widgets[i].Kind is not (Clock or Battery)) Assert.False(s.Overlaps(i)); // added ones never overlap
    }

    [Fact]
    public void Sanitize_Null_Default() => Assert.Equal(DesktopLayout.Default.Widgets, DesktopLayout.Sanitize(null, 1723, 1015).Widgets);
}
