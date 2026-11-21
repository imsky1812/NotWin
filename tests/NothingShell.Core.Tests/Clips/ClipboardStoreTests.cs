using NothingShell.Core.Clips;

namespace NothingShell.Core.Tests.Clips;

public sealed class ClipboardStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nsshell-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new ClipboardStore(_dir);
        Assert.Empty(store.Load());
        IReadOnlyList<ClipItem> items = [ClipItem.OfText("new"), ClipItem.OfImage("3f2a", 1920, 1080), ClipItem.OfText("old é ✓\nline")];
        store.Save(items);
        Assert.Equal(items, store.Load());
    }

    [Fact]
    public void Load_OldTextOnlyFile_StillReads()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "clipboard.json"), "[\"new\",\"old\"]");
        Assert.Equal([ClipItem.OfText("new"), ClipItem.OfText("old")], new ClipboardStore(_dir).Load());
    }

    [Fact]
    public void Load_Corrupt_Empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "clipboard.json"), "{not json");
        Assert.Empty(new ClipboardStore(_dir).Load());
    }
}
