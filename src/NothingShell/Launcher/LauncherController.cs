using System.Windows.Threading;
using NothingShell.Core.Launcher;
using NothingShell.Native;

namespace NothingShell.Launcher;

/// <summary>
/// Opens, closes and toggles the launcher (Win key, dock start/search buttons), launches the chosen item and
/// runs the power actions. The window is created on first use and only hidden afterwards; a one-shot timer
/// trims memory once the launcher has stayed closed for a while.
/// </summary>
public sealed class LauncherController : IDisposable
{
    private static readonly TimeSpan IndexMaxAge = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan TrimAfter = TimeSpan.FromMinutes(5);

    private readonly AppIndex _index;
    private readonly FileSearchService _files;
    private readonly DispatcherTimer _trim;
    private LauncherViewModel? _vm;
    private LauncherWindow? _window;
    private bool _windowClosed;
    private bool _disposed;

    /// <summary>Raised after a launch with the item's display name (Plan 3's notch shows "Opening ‹name›…").</summary>
    public event Action<string>? Launched;

    public LauncherController(AppIndex index, FileSearchService files)
    {
        _index = index;
        _files = files;
        _trim = new DispatcherTimer { Interval = TrimAfter };
        _trim.Tick += (_, _) =>
        {
            _trim.Stop();
            GC.Collect(1, GCCollectionMode.Optimized, blocking: false);
        };
    }

    public bool IsOpen => _window?.IsVisible == true;

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (_disposed || IsOpen) return;
        try
        {
            _trim.Stop();
            _ = _index.RefreshIfStaleAsync(IndexMaxAge);
            EnsureWindow().ShowAndFocus();
        }
        catch (Exception e)
        {
            Log.Error("opening the launcher failed", e);
            try { _window?.HideNow(); } catch { }
        }
    }

    public void Close()
    {
        if (_window is null || !_window.IsVisible) return;
        _window.HideNow();
        _trim.Stop();
        _trim.Start();
    }

    private LauncherWindow EnsureWindow()
    {
        // Belt and braces for I2: if the window was ever destroyed anyway, build a new one instead of failing forever.
        if (_window is not null && !_windowClosed) return _window;
        _windowClosed = false;
        _vm = new LauncherViewModel(new LauncherSources(
            Searchable: () => _index.All,
            MostUsed: _index.MostUsed,
            AllApps: () => _index.Apps,
            Usage: _index.Usage,
            FileSearch: _files.SearchAsync), TimeSpan.FromMilliseconds(150));
        _vm.LaunchRequested += OnLaunchRequested;
        _window = new LauncherWindow(_vm);
        _window.Closed += (_, _) => _windowClosed = true;
        _window.Dismissed += Close;
        _window.PowerRequested += OnPower;
        return _window;
    }

    private void OnLaunchRequested(LaunchItem item)
    {
        Close();
        if (_index.Launch(item))
        {
            try { Launched?.Invoke(item.Name); }
            catch (Exception e) { Log.Error("Launched handler failed", e); }
        }
    }

    private void OnPower(string what)
    {
        Close();
        var ok = what switch
        {
            "sleep" => PowerNative.Sleep(),
            "restart" => PowerNative.Restart(),
            "shutdown" => PowerNative.ShutDown(),
            _ => false,
        };
        Log.Info($"power {what} ok={ok}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _trim.Stop();
        try { _window?.CloseForGood(); } catch { }
    }
}
