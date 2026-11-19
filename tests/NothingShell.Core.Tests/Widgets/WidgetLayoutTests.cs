using NothingShell.Core.Widgets;
using static NothingShell.Core.Widgets.WidgetKind;

namespace NothingShell.Core.Tests.Widgets;

public sealed class WidgetLayoutTests
{
    private const int AreaW = 1200, AreaH = 700;
    private static WidgetPlacement P(WidgetKind k, int w, int h, int x, int y) => new(k, new WidgetSize(w, h), x, y);
    private static int IndexOf(DesktopLayout l, WidgetKind k) => l.Widgets.ToList().FindIndex(p => p.Kind == k);
    private static WidgetPlacement NoId(WidgetPlacement p) => p with { Id = "" };
    private static Func<string> Ids() { var n = 0; return () => $"id{n++}"; }

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
        Assert.Equal(P(Snake, 2, 2, 0, 0), NoId(s.Widgets[snake]));
        Assert.Equal(P(WidgetKind.Timer, 2, 2, 74, 74), NoId(s.Widgets.Single(p => p.Kind == WidgetKind.Timer)));
        Assert.True(s.Overlaps(snake));
    }

    [Fact]
    public void Sanitize_DropsUnknownAndDuplicates()
    {
        var s = DesktopLayout.Sanitize(new DesktopLayout(
            [P((WidgetKind)(-1), 1, 1, 0, 0), P(Clock, 3, 1, 0, 0), P(Clock, 2, 1, 600, 0), P(Battery, 5, 5, 0, 444)]), AreaW, AreaH, Ids());
        Assert.Equal(2, s.Widgets.Count);                                                       // nothing added back
        Assert.Equal(P(Clock, 3, 1, 0, 0), NoId(s.Widgets.Single(p => p.Kind == Clock)));       // first copy kept
        Assert.Equal(new WidgetSize(1, 1), s.Widgets.Single(p => p.Kind == Battery).Size);    // bad size -> default
    }

    [Fact]
    public void Sanitize_Null_IsDefaultSevenWithIds()
    {
        var s = DesktopLayout.Sanitize(null, 1723, 1015, Ids());
        Assert.Equal(DesktopLayout.Default.Widgets, s.Widgets.Select(NoId));
        Assert.Equal(7, s.Widgets.Select(p => p.Id).Distinct().Count());
        Assert.DoesNotContain("", s.Widgets.Select(p => p.Id));
    }

    [Fact]
    public void Sanitize_OldEntries_GetIds_KeepPositions()
    {
        var s = DesktopLayout.Sanitize(new DesktopLayout([P(Clock, 3, 1, 0, 0), P(WidgetKind.Timer, 2, 2, 296, 148)]), AreaW, AreaH, Ids());
        Assert.Equal([P(Clock, 3, 1, 0, 0) with { Id = "id0" }, P(WidgetKind.Timer, 2, 2, 296, 148) with { Id = "id1" }], s.Widgets);
    }

    [Fact]
    public void Sanitize_DoesNotAddRemovedKindsBack() =>
        Assert.Single(DesktopLayout.Sanitize(new DesktopLayout([P(Clock, 3, 1, 0, 0)]), AreaW, AreaH, Ids()).Widgets);

    [Fact]
    public void Sanitize_EmptyStaysEmpty() =>
        Assert.Empty(DesktopLayout.Sanitize(new DesktopLayout([]), AreaW, AreaH, Ids()).Widgets);

    [Fact]
    public void Sanitize_DuplicateIds_GetANewOne()
    {
        var s = DesktopLayout.Sanitize(new DesktopLayout([P(Photo, 2, 2, 0, 0) with { Id = "a" }, P(Photo, 2, 2, 444, 0) with { Id = "a" }]), AreaW, AreaH, Ids());
        Assert.Equal(["a", "id0"], s.Widgets.Select(p => p.Id));
    }

    [Fact]
    public void Sanitize_DropsSecondOneOff_KeepsManyPhotos()
    {
        var s = DesktopLayout.Sanitize(new DesktopLayout(
            [P(Clock, 3, 1, 0, 0), P(Clock, 3, 1, 0, 148), P(Photo, 2, 2, 0, 296), P(Photo, 2, 2, 296, 296), P(Photo, 1, 1, 592, 296)]), 1600, 900, Ids());
        Assert.Single(s.Widgets, p => p.Kind == Clock);
        Assert.Equal(3, s.Widgets.Count(p => p.Kind == Photo));
    }

    [Fact]
    public void Rules_PhotoAndFolderSizes_AndRepeatable()
    {
        Assert.Equal([new(2, 2), new(1, 1), new(3, 3)], WidgetRules.Sizes(Photo));
        Assert.Equal([new(1, 1), new(2, 2)], WidgetRules.Sizes(Folder));
        Assert.True(WidgetRules.IsRepeatable(Photo));
        Assert.True(WidgetRules.IsRepeatable(Folder));
        Assert.False(WidgetRules.IsRepeatable(Clock));
        Assert.Equal([Photo, Folder, Clock, Snake, WidgetKind.Timer, Calendar, Weather, Media, Battery], WidgetRules.PickerOrder);
    }

    [Fact]
    public void Add_PlacesAtFirstFreeSpot()
    {
        var l = DesktopLayout.Sanitize(null, 1600, 900, Ids());
        var (after, id) = l.Add(Photo, 1600, 900, () => "new");
        Assert.Equal("new", id);
        var i = after.IndexOf("new");
        Assert.Equal(new WidgetSize(2, 2), after.Widgets[i].Size);
        Assert.False(after.Overlaps(i));
    }

    [Fact]
    public void Add_NoRoom_TopLeft_Flagged()
    {
        var l = new DesktopLayout([P(Snake, 2, 2, 0, 0) with { Id = "s" }]);
        var (after, id) = l.Add(Folder, 300, 300, () => "f");
        var i = after.IndexOf(id);
        Assert.Equal((0, 0), (after.Widgets[i].X, after.Widgets[i].Y));
        Assert.True(after.Overlaps(i));
    }

    [Fact]
    public void Add_KeepsTheConfig()
    {
        var (after, id) = new DesktopLayout([]).Add(Folder, 1600, 900, () => "f", new WidgetConfig(Picture: "cat"));
        Assert.Equal("cat", after.Widgets[after.IndexOf(id)].Config?.Picture);
    }

    [Fact]
    public void Addable_HidesPresentOneOffs_AlwaysOffersPhotoAndFolder()
    {
        var l = DesktopLayout.Sanitize(null, 1600, 900, Ids());
        Assert.Equal([Photo, Folder], l.Addable());
        var snake = l.Widgets.Single(p => p.Kind == Snake).Id;
        Assert.Equal([Photo, Folder, Snake], l.Remove(snake).Addable());
    }

    [Fact]
    public void Remove_ById()
    {
        var l = new DesktopLayout([P(Photo, 2, 2, 0, 0) with { Id = "a" }, P(Photo, 2, 2, 296, 0) with { Id = "b" }]);
        Assert.Equal(["b"], l.Remove("a").Widgets.Select(p => p.Id));
        Assert.Equal(-1, l.IndexOf("zz"));
        Assert.Same(l, l.Remove("zz"));
    }

    [Fact]
    public void WithConfig_ReplacesOnlyThatEntry()
    {
        var l = new DesktopLayout([P(Photo, 2, 2, 0, 0) with { Id = "a" }, P(Photo, 2, 2, 296, 0) with { Id = "b" }]);
        var after = l.WithConfig("b", new WidgetConfig(Shape: "circle"));
        Assert.Null(after.Widgets[0].Config);
        Assert.Equal("circle", after.Widgets[1].Config?.Shape);
    }

    [Fact]
    public void NewId_Is8LowercaseHex() =>
        Assert.Matches("^[0-9a-f]{8}$", DesktopLayout.NewId(new Random(1)));

    [Fact]
    public void Json_RoundTrip_KeepsIdsAndConfig()
    {
        var l = new DesktopLayout([P(Photo, 2, 2, 0, 0) with { Id = "a", Config = new WidgetConfig("circle", ["p1.jpg"]) },
            P(Folder, 1, 1, 296, 0) with { Id = "b", Config = new WidgetConfig(Picture: "cat", Apps: ["x"]) }]);
        var back = RoundTrip(new ShellSettings { WidgetList = l }).WidgetList!;
        Assert.Equal(["a", "b"], back.Widgets.Select(p => p.Id));
        Assert.Equal("circle", back.Widgets[0].Config?.Shape);
        Assert.Equal(["p1.jpg"], back.Widgets[0].Config?.Photos);
        Assert.Equal(["x"], back.Widgets[1].Config?.Apps);
    }

    [Fact]
    public void Settings_SavesWidgetList_LeavesOldKey()
    {
        var old = new DesktopLayout([P(Clock, 3, 1, 0, 0)]);
        var back = RoundTrip(new ShellSettings { Widgets = old, WidgetList = new DesktopLayout([]) });
        Assert.Single(back.Widgets!.Widgets);
        Assert.Empty(back.WidgetList!.Widgets);
        Assert.Null(RoundTrip(new ShellSettings { Widgets = old }).WidgetList);
    }

    // Through the real settings file (temp directory), with the store's own JSON options.
    private static ShellSettings RoundTrip(ShellSettings settings)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ns-widgets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(dir);
            store.Save(settings);
            return new SettingsStore(dir).Load();
        }
        finally { Directory.Delete(dir, true); }
    }
}
