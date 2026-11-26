using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using NotWin.Core.Widgets;
using NotWin.Native;

namespace NotWin.Widgets;

/// <summary>One reading of the machine: cumulative CPU times, memory load, cumulative bytes down / up, and when.</summary>
public sealed record MeterReading((ulong Idle, ulong Kernel, ulong User) Cpu, int RamPercent, long NetDown, long NetUp, TimeSpan At);

/// <summary>
/// System meters' sampler (Plan 13): every 2 s while started, turns two readings into CPU %, RAM % and bytes per second
/// down / up, and keeps the last 20 CPU values. The first reading only sets the baseline (CPU and network show 0).
/// </summary>
public sealed class MeterSampler : IDisposable
{
    public const int HistoryLength = 20;
    private readonly Func<MeterReading?> _read;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly List<int> _history = [];
    private MeterReading? _last;

    public MeterSampler(Func<MeterReading?> read)
    {
        _read = read;
        _timer.Tick += (_, _) => Sample();
    }

    /// <summary>CPU %, RAM %, bytes/s down, bytes/s up.</summary>
    public event Action<int, int, double, double>? Sampled;

    public IReadOnlyList<int> CpuHistory => _history;

    public bool Running => _timer.IsEnabled;

    public void Start()
    {
        if (Running) return;
        _last = null; // a pause leaves a gap: start a fresh baseline
        Sample();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Sample()
    {
        MeterReading? now;
        try { now = _read(); }
        catch (Exception e)
        {
            Log.Error("meter reading failed", e);
            return;
        }
        if (now is null) return;
        var before = _last;
        _last = now;
        int cpu = 0;
        double down = 0, up = 0;
        if (before is not null)
        {
            cpu = MeterMath.CpuPercent(before.Cpu, now.Cpu);
            var dt = now.At - before.At;
            down = MeterMath.NetRate(before.NetDown, now.NetDown, dt);
            up = MeterMath.NetRate(before.NetUp, now.NetUp, dt);
            _history.Add(cpu);
            if (_history.Count > HistoryLength) _history.RemoveAt(0);
        }
        Sampled?.Invoke(cpu, now.RamPercent, down, up);
    }

    /// <summary>
    /// This PC: GetSystemTimes, GlobalMemoryStatusEx and the byte counters of every network interface that is up
    /// (loopback and tunnels left out). Adapter names are never read out.
    /// </summary>
    public static MeterReading? ReadSystem()
    {
        if (SystemNative.SystemTimes() is not { } cpu || SystemNative.MemoryLoad() is not { } ram) return null;
        long down = 0, up = 0;
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up
                || n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try
            {
                var s = n.GetIPStatistics();
                down += s.BytesReceived;
                up += s.BytesSent;
            }
            catch (NetworkInformationException) { } // an adapter going away mid-read
        }
        return new MeterReading(cpu, ram, down, up, TimeSpan.FromTicks(Stopwatch.GetTimestamp() * TimeSpan.TicksPerSecond / Stopwatch.Frequency));
    }

    public void Dispose() => _timer.Stop();
}
