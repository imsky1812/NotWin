using NothingShell.Core.Notch;
using static NothingShell.Core.Notch.TileKind;
using static NothingShell.Core.Notch.TileSize;

namespace NothingShell.Core.Tests.Notch;

public sealed class DashboardTests
{
    private static TileSpec T(TileKind k, TileSize s) => new(k, s);

    private static DashboardLayout Layout(IReadOnlyList<TileSpec> page1, IReadOnlyList<TileSpec>? page2 = null,
        IReadOnlyList<TileSpec>? removed = null) =>
        new([page1, page2 ?? []], removed ?? []);

    [Fact]
    public void Default_MatchesSpec()
    {
        var d = DashboardLayout.Default;
        Assert.Equal([T(TileKind.Timer, Big), T(Media, Big), T(Clipboard, Big), T(Notes, Big)], d.Pages[0]);
        Assert.Equal(
            [T(Shelf, Big), T(KeepAwake, Wide), T(Weather, Wide), T(GrabText, One), T(Phone, One), T(Color, One),
             T(Snip, One), T(Draw, One), T(Settings, One)],
            d.Pages[1]);
        Assert.Empty(d.Removed);
    }

    [Fact]
    public void CycleSize_FollowsAllowedOrder()
    {
        var l = Layout([T(TileKind.Timer, Big)], [T(KeepAwake, Wide)]);
        l = l.CycleSize(0, 0);
        Assert.Equal(Wide, l.Pages[0][0].Size);
        l = l.CycleSize(0, 0);
        Assert.Equal(One, l.Pages[0][0].Size);
        l = l.CycleSize(0, 0);
        Assert.Equal(Big, l.Pages[0][0].Size);

        l = l.CycleSize(1, 0);
        Assert.Equal(One, l.Pages[1][0].Size);
        l = l.CycleSize(1, 0);
        Assert.Equal(Wide, l.Pages[1][0].Size);
    }

    [Fact]
    public void CycleSize_SkipsSizesThatOverflow()
    {
        // 3 rows full: 4 Big (rows 0-1) + 4 Wide (row 2). Making a Wide tile Big would need a 4th row,
        // so the Wide cycles to One instead (its next allowed size that fits).
        var page = new List<TileSpec> { T(TileKind.Timer, Big), T(Media, Big), T(Clipboard, Big), T(Notes, Big),
            T(KeepAwake, Wide), T(Weather, Wide), T(GrabText, Wide), T(Phone, Wide) };
        var l = Layout(page);
        var weatherBefore = l.Pages[0][5];
        Assert.Equal(Wide, weatherBefore.Size);
        var after = l.CycleSize(0, 5); // Weather: Wide -> One (fits)
        Assert.Equal(One, after.Pages[0][5].Size);
        after = after.CycleSize(0, 5); // One -> Big needs a 4th row: skipped, wraps to Wide
        Assert.Equal(Wide, after.Pages[0][5].Size);
    }

    [Fact]
    public void Remove_ThenAddBack_RestoresAtDefaultSize()
    {
        var l = Layout([T(TileKind.Timer, One), T(Media, Big)]);
        l = l.Remove(0, 0);
        Assert.Equal([T(Media, Big)], l.Pages[0]);
        Assert.Equal([T(TileKind.Timer, One)], l.Removed);
        l = l.AddBack(0);
        Assert.Equal([T(Media, Big), T(TileKind.Timer, Big)], l.Pages[0]);
        Assert.Empty(l.Removed);
    }

    [Fact]
    public void AddBack_RefusedWhenPageFull()
    {
        var full = new List<TileSpec> { T(TileKind.Timer, Big), T(Media, Big), T(Clipboard, Big), T(Notes, Big),
            T(KeepAwake, Wide), T(Weather, Wide), T(GrabText, Wide), T(Phone, Wide) };
        var l = Layout(full, removed: [T(Shelf, Big)]);
        var after = l.AddBack(0);
        Assert.Same(l, after);
    }

    [Fact]
    public void Sanitize_RepairsBadLayouts()
    {
        Assert.Equal(DashboardLayout.Default, DashboardLayout.Sanitize(null));
        Assert.Equal(DashboardLayout.Default, DashboardLayout.Sanitize(new DashboardLayout([], [])));

        var bad = new DashboardLayout(
            [
                [T((TileKind)999, Big), T(TileKind.Timer, Big), T(TileKind.Timer, One), T(KeepAwake, Big)],
                [T(Media, Big), T(Clipboard, Big), T(Notes, Big), T(Shelf, Big), T(Weather, Big), T(GrabText, Big),
                 T(Phone, Wide), T(Color, Wide), T(Snip, Wide), T(Draw, Wide)],
            ],
            [T(Media, Big)]);
        var s = DashboardLayout.Sanitize(bad);

        Assert.Equal([T(TileKind.Timer, Big), T(KeepAwake, Wide)], s.Pages[0]); // unknown dropped, duplicate dropped, size fixed
        Assert.True(GridPacker.Rows(s.Pages[1].Select(t => t.Size).ToList()) <= DashboardLayout.MaxRows);
        var placed = s.Pages.SelectMany(p => p).Select(t => t.Kind).ToList();
        var all = placed.Concat(s.Removed.Select(t => t.Kind)).ToList();
        Assert.Equal(Enum.GetValues<TileKind>().Length, all.Count);       // every kind exactly once
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Contains(s.Removed, t => t.Kind == Settings);             // missing everywhere -> Removed
        Assert.Contains(s.Removed, t => t.Kind == Draw);                 // overflowed page 2 -> Removed
    }
}
