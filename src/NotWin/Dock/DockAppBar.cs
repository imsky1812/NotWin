using System.Windows;
using System.Windows.Interop;
using NotWin.Core;
using NotWin.Native;

namespace NotWin.Dock;

/// <summary>
/// Registers the dock window as a bottom appbar so maximized windows end above it ("keep visible" reserves space
/// like a normal taskbar). Reserves pill 60 + gap 12 + 12 DIP, scaled to the monitor's DPI. UI thread only, except
/// <see cref="Dispose"/> / <see cref="Unregister"/>, which are idempotent and safe from crash handlers.
/// A leaked registration would leave a dead strip at the bottom of the screen, so every exit path must call Unregister.
/// </summary>
public sealed class DockAppBar : IDisposable
{
    public const double ReservedDip = DockWindow.PillHeight + DockWindow.BottomGap + 12;

    private readonly object _gate = new();
    private readonly uint _message = AppBarNative.RegisterCallbackMessage();
    private Window? _dock;
    private HwndSource? _source;
    private nint _hwnd;
    private nint _filteredHwnd;
    private bool _registered;
    private bool _inPosChanged;
    private RectI _granted;

    public bool IsRegistered => _registered;

    /// <summary>Raised (possibly off the UI thread on crash paths) when the granted rect changes; default = released.</summary>
    public event Action<RectI>? GrantedChanged;

    /// <summary>Last rectangle the system granted (physical pixels); default when not registered.</summary>
    public RectI Granted => _granted;

    /// <summary>Registers (or, when already registered, re-applies) the reservation on <paramref name="monitor"/>.</summary>
    public void Register(Window dock, RectI monitor)
    {
        lock (_gate)
        {
            try
            {
                var hwnd = new WindowInteropHelper(dock).EnsureHandle();
                if (hwnd == 0) { Log.Error("appbar: dock has no HWND"); return; }

                if (hwnd != _filteredHwnd)
                {
                    _filteredHwnd = hwnd;
                    var ok = AppBarNative.AllowMessage(hwnd, _message, out var err);
                    Log.Info(ok ? "appbar: callback message allowed through UIPI" : $"appbar: ChangeWindowMessageFilterEx failed (error {err})");
                }
                if (_registered && hwnd != _hwnd) UnregisterCore();
                if (!_registered)
                {
                    if (!AppBarNative.New(hwnd, _message)) { Log.Error("appbar: ABM_NEW failed"); return; }
                    _dock = dock;
                    _hwnd = hwnd;
                    _registered = true;
                    _source = HwndSource.FromHwnd(hwnd);
                    _source?.AddHook(WndProc);
                }

                Apply(monitor);
            }
            catch (Exception e) { Log.Error("appbar register failed", e); }
        }
    }

    /// <summary>Re-reads the primary monitor and re-applies (DPI / display change, ABN_POSCHANGED).</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            if (!_registered) return;
            try { Apply(DockWindowNative.GetPrimaryMonitor().Bounds); }
            catch (Exception e) { Log.Error("appbar refresh failed", e); }
        }
    }

    /// <summary>Explorer restarted and forgot our registration: register again from scratch.</summary>
    public bool ReRegister()
    {
        Window? dock;
        lock (_gate)
        {
            if (!_registered) return false;
            dock = _dock;
            _registered = false; // Explorer already lost it; skip ABM_REMOVE and send ABM_NEW again
            _granted = default;  // the old grant died with Explorer; without this SETPOS is skipped as "unchanged" and nothing is reserved
            try { _source?.RemoveHook(WndProc); } catch (Exception) { }
            _source = null;
        }
        if (dock is not null) Register(dock, DockWindowNative.GetPrimaryMonitor().Bounds);
        Log.Info($"appbar re-registered after TaskbarCreated granted={_granted}");
        return _registered;
    }

    public void Unregister()
    {
        lock (_gate) UnregisterCore();
    }

    public void Dispose() => Unregister();

    private void Apply(RectI monitor)
    {
        var dpi = DockWindowNative.GetPrimaryMonitor().Dpi;
        var height = (int)Math.Ceiling(ReservedDip * dpi / 96.0);
        var before = _granted;
        _granted = AppBarNative.SetBottom(_hwnd, monitor, height, _granted == default ? null : _granted);
        if (_granted != before)
        {
            Log.Info($"appbar set: monitor={monitor} dpi={dpi} height={height} granted={_granted}");
            RaiseGranted();
        }
    }

    private void UnregisterCore()
    {
        if (!_registered) return;
        _registered = false;
        try { AppBarNative.Remove(_hwnd); }
        catch (Exception e) { Log.Error("appbar remove failed", e); }
        try { _source?.RemoveHook(WndProc); } catch (Exception) { /* wrong thread or window gone */ }
        _source = null;
        _granted = default;
        Log.Info("appbar unregistered");
        RaiseGranted();
    }

    private void RaiseGranted()
    {
        try { GrantedChanged?.Invoke(_granted); }
        catch (Exception e) { Log.Error("appbar GrantedChanged handler failed", e); }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _message && (int)wParam == AppBarNative.AbnPosChanged && !_inPosChanged)
        {
            _inPosChanged = true;
            try { Refresh(); }
            finally { _inPosChanged = false; }
        }
        return IntPtr.Zero;
    }
}
