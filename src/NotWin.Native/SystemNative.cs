using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace NotWin.Native;

/// <summary>
/// Small Win32 calls for the Plan 13 widgets: the theme-change broadcast (dark-mode toggle), CPU / memory readings
/// (system meters) and the time since the last input (screen time).
/// </summary>
public static class SystemNative
{
    private const int WmSettingChange = 0x001A, SmtoAbortIfHung = 0x0002;
    private static readonly nint HwndBroadcast = 0xFFFF;

    /// <summary>Tells every top-level window that a setting area changed (e.g. "ImmersiveColorSet" after a theme flip).</summary>
    public static void BroadcastSettingChange(string area) =>
        SendMessageTimeout(HwndBroadcast, WmSettingChange, 0, area, SmtoAbortIfHung, 200, out _);

    /// <summary>Cumulative idle, kernel (including idle) and user CPU time in 100 ns units; null when the call fails.</summary>
    public static (ulong Idle, ulong Kernel, ulong User)? SystemTimes() =>
        GetSystemTimes(out var idle, out var kernel, out var user) ? (idle, kernel, user) : null;

    /// <summary>Physical memory in use, 0–100; null when the call fails.</summary>
    public static int? MemoryLoad()
    {
        var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref m) ? (int)m.MemoryLoad : null;
    }

    /// <summary>Time since the last keyboard or mouse input in this session.</summary>
    public static TimeSpan IdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        // dwTime is a 32-bit tick count: compare in 32 bits so the 49.7-day wrap cancels out.
        var ms = unchecked((uint)Environment.TickCount64 - info.Time);
        return TimeSpan.FromMilliseconds(ms);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint hWnd, int msg, nint wParam, string lParam, int flags, int timeout, out nint result);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
}

/// <summary>
/// Raises <see cref="Changed"/> (on a thread-pool thread) whenever a value of one HKCU key changes, until disposed:
/// RegNotifyChangeKeyValue with an event, re-armed after every change. Only the key's own values are watched, not its
/// subkeys (the mic's consent key gets per-app "last used" writes all the time). A missing key is not watched.
/// Arming and disposing share a lock, and the callback never lets an exception escape: a pool-thread exception would
/// end the shell.
/// </summary>
public sealed class RegistryWatch : IDisposable
{
    private const int NotifyLastSet = 0x4;
    private const int ThreadAgnostic = 0x10000000; // the wait survives the arming pool thread retiring
    private readonly object _gate = new();
    private readonly RegistryKey? _key;
    private readonly AutoResetEvent _signal = new(false);
    private RegisteredWaitHandle? _wait;
    private bool _disposed;

    public RegistryWatch(string userSubKey)
    {
        try { _key = Registry.CurrentUser.OpenSubKey(userSubKey); }
        catch (Exception) { _key = null; }
        if (_key is null) return;
        Arm();
    }

    public event Action? Changed;

    private void Arm()
    {
        lock (_gate)
        {
            if (_disposed || _key is null) return;
            _wait?.Unregister(null);
            _wait = null;
            if (RegNotifyChangeKeyValue(_key.Handle, false, NotifyLastSet | ThreadAgnostic, _signal.SafeWaitHandle, true) != 0) return;
            _wait = ThreadPool.RegisterWaitForSingleObject(_signal, OnSignal, null, Timeout.Infinite, executeOnlyOnce: true);
        }
    }

    private void OnSignal(object? state, bool timedOut)
    {
        try
        {
            lock (_gate) { if (_disposed) return; }
            Changed?.Invoke();
            Arm();
        }
        catch (Exception) { } // disposed mid-callback, or a handler failed: never take the process down
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _wait?.Unregister(null);
            _wait = null;
            _key?.Dispose(); // closing the key ends the pending notification
            _signal.Dispose();
        }
    }

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree, int filter,
        SafeWaitHandle evt, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
