using System.Management;

namespace NothingShell.Panel;

/// <summary>Reads / writes the built-in display's brightness (null when unsupported, e.g. an external monitor).</summary>
public interface IBrightnessBackend
{
    int? Read();
    void Write(int percent);
}

/// <summary>
/// The panel's brightness slider (spec §6.3). WMI calls run off the UI thread; dragging is coalesced to at most one
/// write per 50 ms and the final value is always written. System.Management loads only when this is first used.
/// </summary>
public sealed class BrightnessService
{
    private static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(50);
    private readonly IBrightnessBackend _backend;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<TimeSpan, Action> _schedule;
    private readonly bool _runInline;
    private readonly object _gate = new();
    private DateTimeOffset _lastWrite = DateTimeOffset.MinValue;
    private int? _pending;
    private bool _scheduled;

    /// <param name="backend">Null: WMI (root\wmi WmiMonitorBrightness / WmiMonitorBrightnessMethods).</param>
    /// <param name="runInline">Tests: write on the calling thread instead of the thread pool.</param>
    public BrightnessService(IBrightnessBackend? backend = null, Func<DateTimeOffset>? now = null,
        Action<TimeSpan, Action>? schedule = null, bool runInline = false)
    {
        _backend = backend ?? new WmiBrightness();
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _schedule = schedule ?? ((delay, run) => _ = Task.Delay(delay).ContinueWith(_ => run()));
        _runInline = runInline;
    }

    public Task<int?> GetAsync() => _runInline ? Task.FromResult(SafeRead()) : Task.Run(SafeRead);

    public void Set(int percent)
    {
        var value = Math.Clamp(percent, 0, 100);
        lock (_gate)
        {
            var since = _now() - _lastWrite;
            if (since >= MinGap && !_scheduled)
            {
                _lastWrite = _now();
                Write(value);
                return;
            }
            _pending = value;
            if (_scheduled) return;
            _scheduled = true;
            var wait = MinGap - since;
            _schedule(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, Flush);
        }
    }

    private void Flush()
    {
        int? value;
        lock (_gate)
        {
            value = _pending;
            _pending = null;
            _scheduled = false;
            _lastWrite = _now();
        }
        if (value is { } v) Write(v);
    }

    private void Write(int value)
    {
        if (_runInline) { SafeWrite(value); return; }
        _ = Task.Run(() => SafeWrite(value));
    }

    private int? SafeRead()
    {
        try { return _backend.Read(); }
        catch (Exception e) { Log.Error("brightness read failed", e); return null; }
    }

    private void SafeWrite(int value)
    {
        try { _backend.Write(value); }
        catch (Exception e) { Log.Error("brightness write failed", e); }
    }

    private sealed class WmiBrightness : IBrightnessBackend
    {
        public int? Read()
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject o in searcher.Get())
                using (o) return Convert.ToInt32(o["CurrentBrightness"]);
            return null; // no internal panel: hide the row
        }

        public void Write(int percent)
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject o in searcher.Get())
                using (o) o.InvokeMethod("WmiSetBrightness", [1u, (byte)percent]);
        }
    }
}
