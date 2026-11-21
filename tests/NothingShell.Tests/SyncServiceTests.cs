using System.IO;
using NothingShell.Core;
using NothingShell.Sync;

namespace NothingShell.Tests;

// Plan 9: settings, notes and alarms kept in "<folder>\Nothing Shell sync"; per-PC settings stay; no back-and-forth.
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
}
