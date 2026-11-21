namespace NotWin;

/// <summary>Per-session single-instance guard. The mutex is held in a field for the life of the process.</summary>
public static class SingleInstance
{
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        if (_mutex is not null) return true;
        var mutex = new Mutex(initiallyOwned: true, @"Local\NotWin.Instance", out var createdNew);
        if (!createdNew) { mutex.Dispose(); return false; }
        _mutex = mutex;
        return true;
    }

    /// <summary>Another process in this session holds the named mutex (used to spot a running Nothing Shell).</summary>
    public static bool IsHeld(string name)
    {
        if (!Mutex.TryOpenExisting(name, out var m)) return false;
        m.Dispose();
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
