using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NotWin.Native;

/// <summary>
/// A dedicated background thread with its own message loop, for low-level (LL) hooks. Windows calls an LL
/// hook through the thread that installed it and silently removes the hook if that thread is slow to answer
/// (LowLevelHooksTimeout, at most 1 s). Keeping hooks off the WPF UI thread means a busy UI (or a blocking call
/// into Explorer) can never cost us the Win key or the bottom-edge reveal.
/// </summary>
public sealed unsafe class HookThread : IDisposable
{
    private const uint WmRunQueued = 0x8001; // WM_APP + 1
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _ready = new();
    private volatile uint _threadId;
    private volatile bool _disposed;

    public HookThread(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name };
        _thread.Start();
        _ready.Wait();
    }

    public uint ThreadId => _threadId;

    /// <summary>Runs <paramref name="action"/> on the hook thread and waits for it (rethrows its exception).</summary>
    public void Invoke(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId) { action(); return; }
        Exception? error = null;
        using var done = new ManualResetEventSlim();
        _queue.Enqueue(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
            finally { done.Set(); }
        });
        if (!PInvoke.PostThreadMessage(_threadId, WmRunQueued, default, default))
            throw new InvalidOperationException("hook thread is not accepting work");
        if (!done.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("hook thread did not run the action within 5 s");
        if (error is not null) throw new InvalidOperationException("hook thread action failed", error);
    }

    private void Run()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        MSG msg;
        // Force the message queue to exist before anyone posts to it.
        PInvoke.PeekMessage(&msg, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        _ready.Set();
        while (PInvoke.GetMessage(&msg, HWND.Null, 0, 0) > 0)
        {
            if (msg.hwnd == HWND.Null && msg.message == WmRunQueued)
            {
                while (_queue.TryDequeue(out var a)) a();
                continue;
            }
            PInvoke.TranslateMessage(&msg);
            PInvoke.DispatchMessage(&msg);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PInvoke.PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, default, default);
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
