using NothingShell.Core.Launcher;

namespace NothingShell.Core.Tests.Launcher;

public sealed class LaunchUsageStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-usage-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Record_IncrementsCountAndSetsLast()
    {
        var s = new LaunchUsageStore(_dir);
        s.Record("a", T0);
        s.Record("a", T0.AddMinutes(5));
        var u = s.Load()["a"];
        Assert.Equal(2, u.Count);
        Assert.Equal(T0.AddMinutes(5), u.LastLaunched);
    }

    [Fact]
    public void Record_PersistsAcrossInstances()
    {
        new LaunchUsageStore(_dir).Record("calc", T0);
        Assert.Equal(1, new LaunchUsageStore(_dir).Load()["calc"].Count);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, LaunchUsageStore.FileName), "{not json");
        Assert.Empty(new LaunchUsageStore(_dir).Load());
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty() => Assert.Empty(new LaunchUsageStore(_dir).Load());

    [Fact]
    public void Record_CapsAt500DroppingOldest()
    {
        var s = new LaunchUsageStore(_dir);
        for (var i = 0; i < 501; i++) s.Record($"id{i}", T0.AddSeconds(i));
        var all = s.Load();
        Assert.Equal(LaunchUsageStore.MaxEntries, all.Count);
        Assert.Equal(500, LaunchUsageStore.MaxEntries);
        Assert.False(all.ContainsKey("id0"));
        Assert.True(all.ContainsKey("id500"));
    }
}
