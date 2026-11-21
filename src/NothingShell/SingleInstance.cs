namespace NothingShell;

/// <summary>Per-session single-instance guard. The mutex is held in a field for the life of the process.</summary>
public static class SingleInstance
{
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        if (_mutex is not null) return true;
        var mutex = new Mutex(initiallyOwned: true, @"Local\NothingShell.Instance", out var createdNew);
        if (!createdNew) { mutex.Dispose(); return false; }
        _mutex = mutex;
        return true;
    }

    public static void Release()
    {
        var m = _mutex;
        _mutex = null;
        if (m is null) return;
        try { m.ReleaseMutex(); } catch (ApplicationException) { /* not owned by this thread: process exit frees it */ }
        m.Dispose();
    }
}
