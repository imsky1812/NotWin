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
}
