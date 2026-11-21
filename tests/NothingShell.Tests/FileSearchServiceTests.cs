using NothingShell.Core.Launcher;
using NothingShell.Launcher;

namespace NothingShell.Tests;

public sealed class FileSearchServiceTests
{
    [Fact]
    public async Task Search_ProviderThrows_ReturnsEmpty()
    {
        var s = new FileSearchService((_, _) => throw new InvalidOperationException("provider missing"), recentDir: null);
        Assert.Empty(await s.SearchAsync("report", CancellationToken.None));
    }

    [Fact]
    public async Task Search_StaleQueryResult_IsDiscarded()
    {
        using var cts = new CancellationTokenSource();
        var s = new FileSearchService((q, ct) =>
        {
            cts.Cancel(); // the user typed another character while the index query ran
            return [new LaunchItem(@"C:\r.txt", "r.txt", LaunchKind.File, @"C:\r.txt", [])];
        }, recentDir: null);
        Assert.Empty(await s.SearchAsync("r", cts.Token));
    }

    [Fact]
    public async Task Search_ReturnsIndexResults()
    {
        var s = new FileSearchService((q, _) => [new LaunchItem(@"C:\" + q + ".txt", q + ".txt", LaunchKind.File, @"C:\" + q + ".txt", [])], recentDir: null);
        var r = await s.SearchAsync("budget", CancellationToken.None);
        Assert.Equal("budget.txt", Assert.Single(r).Name);
    }

    [Fact]
    public async Task Search_Whitespace_ReturnsEmptyWithoutQuerying()
    {
        var called = false;
        var s = new FileSearchService((_, _) => { called = true; return []; }, recentDir: null);
        Assert.Empty(await s.SearchAsync("  ", CancellationToken.None));
        Assert.False(called);
    }
}
