namespace NotWin.Core.Tests;

// Plan 12: the first NotWin start copies %AppData%\NothingShell into %AppData%\NotWin (once, never overwriting).
public sealed class DataMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ns-mig-" + Guid.NewGuid().ToString("N"));
    private string Old => Path.Combine(_root, "NothingShell");
    private string New => Path.Combine(_root, "NotWin");

    public void Dispose()
    {
        foreach (var f in Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : [])
            File.SetAttributes(f, FileAttributes.Normal);
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private void Put(string dir, string rel, string text = "x")
    {
        var p = Path.Combine(dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
    }

    [Theory]
    [InlineData(true, false, MigrationStep.Copy)]
    [InlineData(true, true, MigrationStep.None)]
    [InlineData(false, false, MigrationStep.None)]
    [InlineData(false, true, MigrationStep.None)]
    public void Plan_CopyOnlyWhenOldThereAndNewEmpty(bool oldExists, bool newHasFiles, MigrationStep step) =>
        Assert.Equal(step, DataMigration.Plan(oldExists, newHasFiles));

    [Fact]
    public void Plan_NewHasFiles_IsNone() => Assert.Equal(MigrationStep.None, DataMigration.Plan(oldExists: true, newHasFiles: true));

    [Fact]
    public void Plan_NoOld_IsNone() => Assert.Equal(MigrationStep.None, DataMigration.Plan(oldExists: false, newHasFiles: false));

    [Fact]
    public void Run_CopiesTreeSkipsStateAndLogs_WritesMarker()
    {
        Put(Old, "settings.json", "{}");
        Put(Old, @"frames\a\1.jpg");
        Put(Old, "google.dat");
        Put(Old, "taskbar-restore.json");
        Put(Old, "log.txt");
        Put(Old, "LOG.1.txt");
        Put(Old, "draghelper.log");
        var (copied, failed) = DataMigration.Run(Old, New);
        Assert.Equal((3, 0), (copied, failed));
        Assert.True(File.Exists(Path.Combine(New, "settings.json")));
        Assert.True(File.Exists(Path.Combine(New, @"frames\a\1.jpg")));
        Assert.True(File.Exists(Path.Combine(New, "google.dat")));
        Assert.False(File.Exists(Path.Combine(New, "taskbar-restore.json")));
        Assert.False(File.Exists(Path.Combine(New, "log.txt")));
        Assert.False(File.Exists(Path.Combine(New, "LOG.1.txt")));
        Assert.False(File.Exists(Path.Combine(New, "draghelper.log")));
        Assert.True(File.Exists(Path.Combine(New, DataMigration.Marker)));
        Assert.True(File.Exists(Path.Combine(Old, "settings.json"))); // the old folder stays as a backup
    }

    [Fact]
    public void Run_NeverOverwrites()
    {
        Put(Old, "notes.txt", "old");
        Put(New, "notes.txt", "new");
        DataMigration.Run(Old, New);
        Assert.Equal("new", File.ReadAllText(Path.Combine(New, "notes.txt")));
    }

    [Fact]
    public void Run_ReadOnlyTarget_ContinuesAndCounts()
    {
        Put(Old, "a.json");
        Put(Old, @"sub\b.json");
        Directory.CreateDirectory(New);
        // A directory where a file should go: that file cannot be copied; the rest still is.
        Directory.CreateDirectory(Path.Combine(New, "a.json"));
        var (copied, failed) = DataMigration.Run(Old, New);
        Assert.Equal(1, failed);
        Assert.Equal(1, copied);
        Assert.True(File.Exists(Path.Combine(New, @"sub\b.json")));
    }
}
