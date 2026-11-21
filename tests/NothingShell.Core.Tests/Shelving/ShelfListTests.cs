using NothingShell.Core.Shelving;

namespace NothingShell.Core.Tests.Shelving;

public sealed class ShelfListTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nsshell-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Add_DedupesCaseInsensitive()
    {
        var s = new ShelfList();
        Assert.True(s.Add(@"C:\Docs\a.pdf"));
        Assert.False(s.Add(@"c:\docs\A.PDF"));
        Assert.True(s.Add(@"C:\Docs\b.png"));
        Assert.Equal([@"C:\Docs\a.pdf", @"C:\Docs\b.png"], s.Paths);
    }

    [Fact]
    public void Add_FullIgnored()
    {
        var s = new ShelfList(cap: 2);
        s.Add("a"); s.Add("b");
        Assert.False(s.Add("c"));
        Assert.Equal(2, s.Paths.Count);
        Assert.Equal(20, new ShelfList().Cap);
    }

    [Fact]
    public void Add_Blank_Ignored() => Assert.False(new ShelfList().Add("  "));

    [Fact]
    public void Remove_OutOfRange_Ignored()
    {
        var s = new ShelfList();
        s.Add("a");
        s.Remove(3);
        s.Remove(0);
        Assert.Empty(s.Paths);
    }

    [Fact]
    public void From_DropsMissing()
    {
        var s = ShelfList.From(["keep", "gone", "keep2"], p => p != "gone");
        Assert.Equal(["keep", "keep2"], s.Paths);
        Assert.Empty(ShelfList.From(null, _ => true).Paths);
    }

    [Fact]
    public void Store_RoundTrips()
    {
        var store = new ShelfStore(_dir);
        Assert.Empty(store.Load());
        store.Save([@"C:\x\é.txt"]);
        Assert.Equal([@"C:\x\é.txt"], store.Load());
    }
}
