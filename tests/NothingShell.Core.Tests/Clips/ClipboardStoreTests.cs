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
        store.Save(["new", "old é ✓\nline"]);
        Assert.Equal(["new", "old é ✓\nline"], store.Load());
    }

    [Fact]
    public void Load_Corrupt_Empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "clipboard.json"), "{not json");
        Assert.Empty(new ClipboardStore(_dir).Load());
    }
}
