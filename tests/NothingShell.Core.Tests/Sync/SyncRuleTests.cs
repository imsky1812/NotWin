using NothingShell.Core.Sync;

namespace NothingShell.Core.Tests.Sync;

// Plan 9: settings, notes and alarms kept in a folder the user picks; the newer copy wins when both PCs changed it.
public sealed class SyncRuleTests
{
    private static readonly DateTime T0 = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);
    private static SyncSide Side(string? hash, int minutes = 0) => hash is null ? new SyncSide(false, null, default) : new SyncSide(true, hash, T0.AddMinutes(minutes));

    [Fact]
    public void OnlyLocalChanged_Push() => Assert.Equal(SyncAction.Push, SyncRule.Decide(Side("new"), Side("old", 5), "old"));

    [Fact]
    public void OnlyRemoteChanged_Pull() => Assert.Equal(SyncAction.Pull, SyncRule.Decide(Side("old", 5), Side("new"), "old"));

    [Fact]
    public void BothChanged_NewerWins()
    {
        Assert.Equal(SyncAction.Pull, SyncRule.Decide(Side("a", 1), Side("b", 2), "old"));
        Assert.Equal(SyncAction.Push, SyncRule.Decide(Side("a", 3), Side("b", 2), "old"));
    }

    [Fact]
    public void FirstSync_OneSideMissing()
    {
        Assert.Equal(SyncAction.Push, SyncRule.Decide(Side("a"), Side(null), null));
        Assert.Equal(SyncAction.Pull, SyncRule.Decide(Side(null), Side("b"), null));
        Assert.Equal(SyncAction.None, SyncRule.Decide(Side(null), Side(null), null));
        Assert.Equal(SyncAction.Pull, SyncRule.Decide(Side("a", 1), Side("b", 2), null)); // both there: newer wins
    }

    [Fact]
    public void SameHash_None() => Assert.Equal(SyncAction.None, SyncRule.Decide(Side("x", 1), Side("x", 9), "old"));

    [Fact]
    public void RemoteDeleted_LocalUnchanged_PushesItBack() =>
        Assert.Equal(SyncAction.Push, SyncRule.Decide(Side("x"), Side(null), "x"));

    [Fact]
    public void Merge_KeepsPerPcFields()
    {
        var mine = new ShellSettings { StartWithWindows = false, SyncFolder = @"C:\Mine", PinsImported = true, KeepDockVisible = false, SnakeBest = 3 };
        var theirs = new ShellSettings { StartWithWindows = true, SyncFolder = @"D:\Theirs", PinsImported = false, KeepDockVisible = true, SnakeBest = 9 };
        var merged = SyncRule.Merge(mine, theirs);
        Assert.False(merged.StartWithWindows);
        Assert.Equal(@"C:\Mine", merged.SyncFolder);
        Assert.True(merged.PinsImported);
        Assert.True(merged.KeepDockVisible);
        Assert.Equal(9, merged.SnakeBest);
    }

    [Fact]
    public void Hash_IsHexSha256() =>
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", SyncRule.Hash("abc"u8.ToArray()));
}
