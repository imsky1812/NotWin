using NothingShell.Core;

namespace NothingShell.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nsshell-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = new SettingsStore(_dir).Load();
        Assert.False(s.KeepDockVisible);
        Assert.True(s.StartWithWindows);
        Assert.Equal("M", s.WidgetScale);
        Assert.Empty(s.PinnedApps);
        Assert.False(s.PinsImported);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SettingsStore(_dir);
        var pin = new PinnedApp("app1", "Notepad", @"C:\Windows\notepad.exe");
        store.Save(new ShellSettings { KeepDockVisible = true, PinnedApps = [pin] });

        var loaded = store.Load();
        Assert.True(loaded.KeepDockVisible);
        Assert.Equal(new[] { pin }, loaded.PinnedApps.ToArray());
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsAndKeepsBadCopy()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not json");
        File.WriteAllText(Path.Combine(_dir, "settings.json.bad"), "older");

        var s = new SettingsStore(_dir).Load();

        Assert.False(s.KeepDockVisible);
        Assert.True(s.StartWithWindows);
        Assert.False(File.Exists(Path.Combine(_dir, "settings.json")));
        Assert.Equal("{ not json", File.ReadAllText(Path.Combine(_dir, "settings.json.bad")));
    }

    [Fact]
    public void Load_LockedFile_ReturnsDefaultsWithoutRenaming()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{\"keepDockVisible\":true}");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var s = new SettingsStore(_dir).Load();
            Assert.False(s.KeepDockVisible);
        }
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".bad"));
    }

    [Fact]
    public void Save_AfterFailedLoad_DoesNotOverwrite()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{\"keepDockVisible\":true}");
        var store = new SettingsStore(_dir);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            store.Load();

        store.Save(new ShellSettings());

        Assert.Contains("true", File.ReadAllText(path));
        Assert.True(store.Load().KeepDockVisible); // lock released: load succeeds, saves allowed again
        store.Save(new ShellSettings { WidgetScale = "L" });
        Assert.Equal("L", store.Load().WidgetScale);
    }

    [Fact]
    public void Load_CorruptFile_WithUnmovableBad_StillReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ nope");
        var bad = Path.Combine(_dir, "settings.json.bad");
        File.WriteAllText(bad, "old");
        using (new FileStream(bad, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.True(new SettingsStore(_dir).Load().StartWithWindows);
    }

    [Fact]
    public void LastLoadSucceeded_ReflectsLoadOutcome()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(_dir);
        store.Load();
        Assert.True(store.LastLoadSucceeded); // missing file is a normal first run

        File.WriteAllText(path, "{\"keepDockVisible\":true}");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store.Load();
            Assert.False(store.LastLoadSucceeded);
        }

        store.Load();
        Assert.True(store.LastLoadSucceeded);
    }
}
