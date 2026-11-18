using System.IO;
using System.Windows.Threading;
using NothingShell.Core;
using NothingShell.Native;

namespace NothingShell.Dock;

/// <summary>
/// Keeps the list of app windows the dock shows. WinEvent hooks (create/destroy/show/hide/name change/cloak/
/// foreground) only schedule a refresh; the refresh itself (EnumWindows + per-window identity) runs on the UI
/// thread at most once per 100 ms. There is no polling. Create, use and dispose on the UI thread.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private const string FrameHostClass = "ApplicationFrameWindow";
    private const string CoreWindowClass = "Windows.UI.Core.CoreWindow";
    private const string FrameHostExe = "applicationframehost.exe";

    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly DispatcherTimer _debounce;
    private readonly Dictionary<uint, string?> _exeByPid = [];
    private readonly Dictionary<nint, string> _aumidByHwnd = [];
    // Stable ordering: windows and apps keep the position they were first seen at, so the dock does not
    // reshuffle every time the z-order changes.
    private readonly Dictionary<nint, long> _windowOrder = [];
    private readonly Dictionary<string, long> _appOrder = new(StringComparer.OrdinalIgnoreCase);
    private long _counter;
    private WindowEventsHook? _hook;
    private bool _disposed;

    public WindowTracker()
    {
        _debounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };
    }

    public IReadOnlyList<AppWindow> Current { get; private set; } = [];

    /// <summary>Raised on the UI thread after the list actually changed.</summary>
    public event Action? Changed;

    public void Start()
    {
        Refresh();
        _hook = new WindowEventsHook(Schedule);
    }

    // Called from the WinEvent callback: must stay tiny.
    private void Schedule()
    {
        if (_disposed || _debounce.IsEnabled) return;
        _debounce.Start();
    }

    private void Refresh()
    {
        if (_disposed) return;
        try
        {
            var next = Build();
            if (next.SequenceEqual(Current)) return;
            Current = next;
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            Log.Error("window refresh failed", e);
        }
    }

    private List<AppWindow> Build()
    {
        var raw = WindowListNative.EnumerateAppWindows(_ownPid);
        var foreground = WindowListNative.GetForegroundRoot();

        var live = new HashSet<nint>();
        var result = new List<(AppWindow Window, long WinOrder)>();
        // EnumWindows is top-first; walk bottom-first so first-seen order at startup is stable.
        for (var i = raw.Count - 1; i >= 0; i--)
        {
            var w = raw[i];
            if (string.IsNullOrEmpty(w.Title)) continue; // untitled: not a user-facing window
            live.Add(w.Handle);
            if (!_windowOrder.ContainsKey(w.Handle)) _windowOrder[w.Handle] = _counter++;

            var (appId, exe) = Resolve(w);
            if (!_appOrder.ContainsKey(appId)) _appOrder[appId] = _windowOrder[w.Handle];
            result.Add((new AppWindow(w.Handle, appId, w.Title, exe, w.Handle == foreground), _windowOrder[w.Handle]));
        }

        Prune(live, result.Select(r => r.Window.AppId));
        result.Sort((a, b) =>
        {
            var c = _appOrder[a.Window.AppId].CompareTo(_appOrder[b.Window.AppId]);
            return c != 0 ? c : a.WinOrder.CompareTo(b.WinOrder);
        });
        return result.ConvertAll(r => r.Window);
    }

    private (string AppId, string Exe) Resolve(RawWindow w)
    {
        var exe = ExeOf(w.Pid);
        var isFrame = w.Class == FrameHostClass &&
                      exe is not null && Path.GetFileName(exe).Equals(FrameHostExe, StringComparison.OrdinalIgnoreCase);
        if (isFrame)
        {
            // The real app is the process that owns the hosted CoreWindow, not ApplicationFrameHost.
            var core = WindowListNative.FindChild(w.Handle, CoreWindowClass);
            if (core != 0) exe = ExeOf(WindowListNative.GetPid(core)) ?? exe;
        }

        if (!_aumidByHwnd.TryGetValue(w.Handle, out var aumid))
        {
            var found = WindowListNative.GetAppUserModelId(w.Handle);
            if (found is not null) _aumidByHwnd[w.Handle] = aumid = found; // windows without one are re-queried
        }

        exe ??= "";
        var appId = aumid ?? (exe.Length > 0 ? exe.ToLowerInvariant() : $"pid:{w.Pid}");
        return (appId, exe);
    }

    private string? ExeOf(uint pid)
    {
        if (_exeByPid.TryGetValue(pid, out var cached)) return cached;
        return _exeByPid[pid] = WindowListNative.GetProcessPath(pid);
    }

    private void Prune(HashSet<nint> liveHwnds, IEnumerable<string> liveApps)
    {
        foreach (var k in _windowOrder.Keys.Where(k => !liveHwnds.Contains(k)).ToList()) _windowOrder.Remove(k);
        foreach (var k in _aumidByHwnd.Keys.Where(k => !liveHwnds.Contains(k)).ToList()) _aumidByHwnd.Remove(k);
        if (_exeByPid.Count > 256) _exeByPid.Clear(); // pids get reused; keep the cache bounded
        var apps = new HashSet<string>(liveApps, StringComparer.OrdinalIgnoreCase);
        foreach (var k in _appOrder.Keys.Where(k => !apps.Contains(k)).ToList()) _appOrder.Remove(k);
    }

    public void Dispose()
    {
        _disposed = true;
        _debounce.Stop();
        _hook?.Dispose();
        _hook = null;
    }
}
