namespace NothingShell.Core.Tests;

public sealed class NotesStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nsshell-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new NotesStore(_dir);
        const string text = "Buy milk ✓\r\nCall Ana — 10:00\n\nünïcode";
        store.Save(text);
        Assert.Equal(text, store.Load());
        store.Save("second");
        Assert.Equal("second", store.Load());
        Assert.Single(Directory.GetFiles(_dir)); // no temp files left behind
    }

    [Fact]
    public void Load_Missing_Empty() => Assert.Equal("", new NotesStore(_dir).Load());
}
