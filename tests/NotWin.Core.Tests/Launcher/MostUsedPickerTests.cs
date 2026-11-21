using NotWin.Core.Launcher;

namespace NotWin.Core.Tests.Launcher;

public sealed class MostUsedPickerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static LaunchItem App(string id) => new(id, id.ToUpperInvariant(), LaunchKind.App, "shell:AppsFolder\\" + id, []);
    private static readonly IReadOnlyList<LaunchItem> Apps = new[] { "a", "b", "c", "d", "e", "f", "g", "h" }.Select(App).ToList();
    private static readonly IReadOnlyDictionary<string, LaunchUsage> NoUsage = new Dictionary<string, LaunchUsage>();

    private static Dictionary<string, LaunchUsage> Usage(params (string Id, int Count, int Min)[] u)
        => u.ToDictionary(x => x.Id, x => new LaunchUsage(x.Id, x.Count, T0.AddMinutes(x.Min)));

    [Fact]
    public void Pick_UsesLaunchCountsFirst()
    {
        var r = MostUsedPicker.Pick(Apps, Usage(("c", 2, 0), ("e", 9, 0), ("b", 2, 5)), []);
        Assert.Equal(["e", "b", "c"], r.Take(3).Select(i => i.Id));
    }

    [Fact]
    public void Pick_FillsFromUserAssistByLastRun()
    {
        var recent = new UserAssistEntry[] { new("D", 3, T0), new("G", 1, T0.AddHours(1)), new("zzz", 4, T0.AddHours(2)) };
        var r = MostUsedPicker.Pick(Apps, NoUsage, recent);
        Assert.Equal(["g", "d"], r.Take(2).Select(i => i.Id));
    }

    [Fact]
    public void Pick_IgnoresUsageForMissingApps()
    {
        var r = MostUsedPicker.Pick(Apps, Usage(("gone", 99, 0), ("h", 1, 0)), []);
        Assert.Equal("h", r[0].Id);
        Assert.DoesNotContain(r, i => i.Id == "gone");
    }

    [Fact]
    public void Pick_FillsRemainingAlphabetically()
    {
        var r = MostUsedPicker.Pick(Apps, Usage(("h", 1, 0)), []);
        Assert.Equal(6, r.Count);
        Assert.Equal(["h", "a", "b", "c", "d", "e"], r.Select(i => i.Id));
    }

    [Fact]
    public void Pick_NoDuplicates()
    {
        var r = MostUsedPicker.Pick(Apps, Usage(("a", 3, 0)), [new UserAssistEntry("a", 5, T0)]);
        Assert.Equal(r.Count, r.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public void Pick_FewerAppsThanCount_ReturnsAll() => Assert.Equal(2, MostUsedPicker.Pick(Apps.Take(2).ToList(), NoUsage, []).Count);
}
