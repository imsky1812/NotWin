using NothingShell.Core.Widgets;

namespace NothingShell.Core.Tests.Widgets;

public sealed class PhotoAndFolderTests
{
    private static readonly DateTime T0 = new(2026, 1, 1);

    [Fact]
    public void Append_SortsBatchByDateTaken()
    {
        var (list, skipped) = PhotoList.Append(["old.jpg"], [("b.jpg", T0.AddDays(2)), ("a.jpg", T0), ("c.jpg", T0.AddDays(1))]);
        Assert.Equal(["old.jpg", "a.jpg", "c.jpg", "b.jpg"], list);
        Assert.Equal(0, skipped);
    }

    [Fact]
    public void Append_StopsAt24_ReportsSkipped()
    {
        var existing = Enumerable.Range(0, 20).Select(i => $"e{i}.jpg").ToList();
        var batch = Enumerable.Range(0, 10).Select(i => ($"n{i}.jpg", T0.AddMinutes(i))).ToList();
        var (list, skipped) = PhotoList.Append(existing, batch);
        Assert.Equal(24, list.Count);
        Assert.Equal(6, skipped);
        Assert.Equal(24, PhotoList.Max);
    }

    [Fact]
    public void Next_Wraps()
    {
        Assert.Equal(0, PhotoList.Next(2, 3));
        Assert.Equal(1, PhotoList.Next(0, 3));
        Assert.Equal(0, PhotoList.Next(0, 1));
        Assert.Equal(0, PhotoList.Next(5, 0));
    }

    [Fact]
    public void Add_IgnoresDuplicates()
    {
        Assert.Equal(["a", "b"], FolderApps.Add(["a"], "b"));
        Assert.Equal(["a", "b"], FolderApps.Add(["a", "b"], "a"));
        Assert.Equal(["b"], FolderApps.Remove(["a", "b"], "a"));
    }

    private sealed record App(string Id);

    [Fact]
    public void Resolve_SkipsMissingApps()
    {
        var installed = new Dictionary<string, App> { ["a"] = new("a"), ["c"] = new("c") };
        var shown = FolderApps.Resolve(["a", "b", "c"], id => installed.GetValueOrDefault(id));
        Assert.Equal([new App("a"), new App("c")], shown);
    }

    [Fact]
    public void DotArt_EveryPictureIs11x11()
    {
        // Plan 9: eight more pictures after the first eight (rocket … ghost), all 11 × 11.
        Assert.Equal(["cat", "folder", "plant", "gamepad", "note", "camera", "cup", "star",
            "rocket", "heart", "moon", "sun", "code", "book", "bolt", "ghost"], DotArt.Pictures);
        foreach (var name in DotArt.Pictures)
        {
            var rows = DotArt.Rows(name);
            Assert.Equal(11, rows.Length);
            Assert.All(rows, r => Assert.Equal(11, r.Length));
            Assert.All(rows, r => Assert.Matches("^[#.]+$", r));
            Assert.True(rows.Sum(r => r.Count(c => c == '#')) >= 12, name);
        }
    }

    [Fact]
    public void DotArt_NextCyclesAndUnknownIsFolder()
    {
        Assert.Equal("folder", DotArt.Next("cat"));
        Assert.Equal("rocket", DotArt.Next("star"));
        Assert.Equal("cat", DotArt.Next("ghost"));
        Assert.Equal(DotArt.Rows("folder"), DotArt.Rows("nope"));
        Assert.Contains(DotArt.Random(new Random(3)), DotArt.Pictures);
    }
}
