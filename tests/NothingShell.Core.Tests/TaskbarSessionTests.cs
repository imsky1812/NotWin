using NothingShell.Core;

namespace NothingShell.Core.Tests;

public sealed class TaskbarSessionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nsshell-tests-" + Guid.NewGuid().ToString("N"));

    public TaskbarSessionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void ResolveOriginal_NoLeftover_UsesCurrent()
    {
        Assert.False(TaskbarSession.ResolveOriginal(null, false).WasAutoHide);
        Assert.True(TaskbarSession.ResolveOriginal(null, true).WasAutoHide);
    }

    [Fact]
    public void ResolveOriginal_PrefersSavedStateFromCrashedSession()
    {
        var r = TaskbarSession.ResolveOriginal(new TaskbarRestoreState(false), true);
        Assert.False(r.WasAutoHide);
    }

    [Fact]
    public void RestoreStore_DeleteThenTryLoad_ReturnsFalse()
    {
        var store = new TaskbarRestoreStore(_dir);
        store.Save(new TaskbarRestoreState(true));
        Assert.True(store.TryLoad(out var s));
        Assert.True(s.WasAutoHide);

        store.Delete();
        Assert.False(store.TryLoad(out _));
        store.Delete(); // idempotent
    }

    [Fact]
    public void RestoreStore_CorruptFile_ReturnsFalse()
    {
        File.WriteAllText(Path.Combine(_dir, "taskbar-restore.json"), "garbage");
        Assert.False(new TaskbarRestoreStore(_dir).TryLoad(out _));
    }
}
