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

    // A log line written before the migration runs must not count as "the new folder already has data".
    [Fact]
    public void HasData_IgnoresLogsAndMissingFolder()
    {
        Assert.False(DataMigration.HasData(New));
        Put(New, "log.txt");
        Assert.False(DataMigration.HasData(New));
        Put(New, "settings.json");
        Assert.True(DataMigration.HasData(New));
    }

    // Review: a copy cut short (crash, power loss) must be finished on the next start, not left half done.
    [Fact]
    public void HasData_MigrationInProgress_IsNoData()
    {
        Put(New, "settings.json");
        Put(New, DataMigration.InProgressMarker);
        Assert.False(DataMigration.HasData(New));
    }

    [Fact]
    public void Run_Finished_LeavesOnlyTheDoneMarker()
    {
        Put(Old, "settings.json");
        DataMigration.Run(Old, New);
        Assert.False(File.Exists(Path.Combine(New, DataMigration.InProgressMarker)));
        Assert.True(File.Exists(Path.Combine(New, DataMigration.Marker)));
        Assert.True(DataMigration.HasData(New));
    }

    // Review: an unreadable folder in the old data must not stop the rest of the copy.
    [Fact]
    public void Run_UnreadableSourceFolder_CopiesTheRest()
    {
        if (!OperatingSystem.IsWindows()) return;
        Put(Old, "settings.json");
        Put(Old, @"locked\x.txt");
        var locked = new DirectoryInfo(Path.Combine(Old, "locked"));
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(me,
            System.Security.AccessControl.FileSystemRights.ListDirectory | System.Security.AccessControl.FileSystemRights.ReadData,
            System.Security.AccessControl.AccessControlType.Deny);
        var acl = locked.GetAccessControl();
        acl.AddAccessRule(deny);
        locked.SetAccessControl(acl);
        try
        {
            var (copied, _) = DataMigration.Run(Old, New);
            Assert.Equal(1, copied);
            Assert.True(File.Exists(Path.Combine(New, "settings.json")));
            Assert.True(File.Exists(Path.Combine(New, DataMigration.Marker)));
        }
        finally
        {
            acl = locked.GetAccessControl();
            acl.RemoveAccessRule(deny);
            locked.SetAccessControl(acl);
        }
    }
}
