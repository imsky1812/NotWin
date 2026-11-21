using System.IO;
using NotWin.Core;
using NotWin.Sync;

namespace NotWin.Tests;

// Plan 9: settings, notes and alarms kept in "<folder>\NotWin sync"; per-PC settings stay; no back-and-forth.
public sealed class SyncServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ns-sync-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(_root, "local");
    private string Folder => Path.Combine(_root, "onedrive");
    private string Remote => Path.Combine(Folder, SyncService.SubFolder);

    public SyncServiceTests()
    {
        Directory.CreateDirectory(Local);
        Directory.CreateDirectory(Folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private SyncService Service(string? folder = "default") => new(Local, () => folder == "default" ? Folder : folder);

    [Fact]
    public void FirstRun_PushesLocalFiles()
    {
        new SettingsStore(Local).Save(new ShellSettings { KeepDockVisible = true, SyncFolder = Folder });
        new NotesStore(Local).Save("milk");
        var outcome = Service().RunOnce();
        Assert.True(outcome.Reached);
        Assert.Equal(["notes.txt", "settings.json"], outcome.Pushed.Order());
        Assert.Equal("milk", File.ReadAllText(Path.Combine(Remote, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(Remote, "settings.json")));
    }

    [Fact]
    public void RemoteNewer_IsPulled_AndPerPcFieldsKept()
    {
        new SettingsStore(Local).Save(new ShellSettings { StartWithWindows = false, SyncFolder = Folder, KeepDockVisible = false });
        File.SetLastWriteTimeUtc(Path.Combine(Local, "settings.json"), DateTime.UtcNow.AddHours(-1));
        Directory.CreateDirectory(Remote);
        new SettingsStore(Remote).Save(new ShellSettings { StartWithWindows = true, SyncFolder = @"Z:\elsewhere", KeepDockVisible = true, SnakeBest = 40 });
        var outcome = Service().RunOnce();
        Assert.Contains("settings.json", outcome.Pulled);
        var mine = new SettingsStore(Local).Load();
        Assert.True(mine.KeepDockVisible);
        Assert.Equal(40, mine.SnakeBest);
        Assert.False(mine.StartWithWindows);
        Assert.Equal(Folder, mine.SyncFolder);
        // The merged file differs from the folder's only in per-PC fields: nothing to copy back.
        var again = Service().RunOnce();
        Assert.Empty(again.Pulled);
        Assert.Empty(again.Pushed);
    }

    [Fact]
    public void FolderMissing_NotReached_LocalUntouched()
    {
        new NotesStore(Local).Save("keep me");
        var outcome = Service(Path.Combine(_root, "gone")).RunOnce();
        Assert.False(outcome.Reached);
        Assert.Equal("keep me", new NotesStore(Local).Load());
        Assert.False(Directory.Exists(Path.Combine(_root, "gone")));
    }

    [Fact]
    public void NoFolderChosen_NotReached() => Assert.False(Service(null).RunOnce().Reached);

    [Fact]
    public void NoChange_NothingCopied()
    {
        new NotesStore(Local).Save("a");
        Service().RunOnce();
        var second = Service().RunOnce();
        Assert.Empty(second.Pulled);
        Assert.Empty(second.Pushed);
    }

    [Fact]
    public void LocalEdit_AfterSync_IsPushed()
    {
        new NotesStore(Local).Save("a");
        Service().RunOnce();
        new NotesStore(Local).Save("b");
        Assert.Equal(["notes.txt"], Service().RunOnce().Pushed);
        Assert.Equal("b", File.ReadAllText(Path.Combine(Remote, "notes.txt")));
    }

    // Review: a second PC joining finds the folder's copy and takes it, even though its own file is newer.
    [Fact]
    public void Joining_FolderCopyWins()
    {
        Directory.CreateDirectory(Remote);
        new SettingsStore(Remote).Save(new ShellSettings { KeepDockVisible = true, SnakeBest = 77 });
        File.SetLastWriteTimeUtc(Path.Combine(Remote, "settings.json"), DateTime.UtcNow.AddHours(-2));
        new SettingsStore(Local).Save(new ShellSettings { KeepDockVisible = false, SyncFolder = Folder });
        var outcome = Service().RunOnce();
        Assert.Contains("settings.json", outcome.Pulled);
        Assert.Equal(77, new SettingsStore(Local).Load().SnakeBest);
    }

    // Review: a sync folder that went away (OneDrive unlinked) is never recreated.
    [Fact]
    public void WatchDirectory_MissingRoot_IsNull_AndNothingCreated()
    {
        var gone = Path.Combine(_root, "gone");
        Assert.Null(Service(gone).WatchDirectory());
        Assert.False(Directory.Exists(gone));
        Assert.Equal(Remote, Service().WatchDirectory());
        Assert.True(Directory.Exists(Remote));
    }

    // Review: an online-only OneDrive file would be downloaded while the shell waits: skipped until it is local.
    [Fact]
    public void OnlineOnlyFile_IsSkipped()
    {
        Directory.CreateDirectory(Remote);
        var remote = Path.Combine(Remote, "notes.txt");
        File.WriteAllText(remote, "from the other PC");
        File.SetAttributes(remote, FileAttributes.Offline);
        new NotesStore(Local).Save("mine");
        var outcome = Service().RunOnce();
        Assert.DoesNotContain("notes.txt", outcome.Pulled);
        Assert.DoesNotContain("notes.txt", outcome.Pushed);
        Assert.Equal("mine", new NotesStore(Local).Load());
        File.SetAttributes(remote, FileAttributes.Normal);
    }

    // Review minor (re-graded: alarms lost on both PCs): a half-written alarms file in the folder is not pulled.
    [Fact]
    public void BrokenAlarmsFile_IsNotPulled()
    {
        Directory.CreateDirectory(Remote);
        File.WriteAllText(Path.Combine(Remote, "alarms.json"), "[{ \"id\": \"a\", ");
        var outcome = Service().RunOnce();
        Assert.DoesNotContain("alarms.json", outcome.Pulled);
        Assert.False(File.Exists(Path.Combine(Local, "alarms.json")));
    }
}
