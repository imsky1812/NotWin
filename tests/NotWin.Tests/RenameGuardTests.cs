using NotWin.Startup;

namespace NotWin.Tests;

// Plan 12 review: NotWin must not run next to a still-running Nothing Shell (two docks, and a migration of a folder the
// old shell is still writing).
public sealed class RenameGuardTests
{
    [Fact]
    public void OldInstanceMutex_IsTheNameNothingShellUsed() =>
        Assert.Equal(@"Local\NothingShell.Instance", LegacyNames.InstanceMutex);

    [Fact]
    public void IsHeld_SeesAMutexOnlyWhileItExists()
    {
        var name = @"Local\NotWin.Test." + Guid.NewGuid().ToString("N");
        Assert.False(SingleInstance.IsHeld(name));
        using (var m = new Mutex(initiallyOwned: false, name))
            Assert.True(SingleInstance.IsHeld(name));
        Assert.False(SingleInstance.IsHeld(name));
    }
}
