using System.Globalization;

namespace NotWin.Core.Widgets;

/// <summary>System meters (Plan 13): CPU from GetSystemTimes deltas, network rates, dot-bar fill.</summary>
public static class MeterMath
{
    /// <summary>
    /// Busy share of the time between two GetSystemTimes readings, 0–100. Kernel time includes idle time, so the total
    /// is kernel + user and busy is total − idle. Counters that went backwards give 0.
    /// </summary>
    public static int CpuPercent((ulong Idle, ulong Kernel, ulong User) before, (ulong Idle, ulong Kernel, ulong User) after)
    {
        if (after.Idle < before.Idle || after.Kernel < before.Kernel || after.User < before.User) return 0;
        double idle = after.Idle - before.Idle;
        double total = after.Kernel - before.Kernel + (after.User - before.User);
        if (total <= 0) return 0;
        return (int)Math.Clamp(Math.Round((total - idle) / total * 100), 0, 100);
    }

    /// <summary>Bytes per second between two counter readings; 0 when the counter went back (reset / wrap) or no time passed.</summary>
    public static double NetRate(long bytesBefore, long bytesAfter, TimeSpan dt) =>
        bytesAfter < bytesBefore || dt <= TimeSpan.Zero ? 0 : (bytesAfter - bytesBefore) / dt.TotalSeconds;

    /// <summary>
    /// Bytes moved between two readings of per-interface counters (id → cumulative down / up). Only interfaces present
    /// in both count, so an adapter coming up (bringing its lifetime counters) or going away is not a spike; a counter
    /// that went back (reset / wrap) counts 0.
    /// </summary>
    public static (long Down, long Up) NetDelta(IReadOnlyDictionary<string, (long Down, long Up)> before,
        IReadOnlyDictionary<string, (long Down, long Up)> after)
    {
        long down = 0, up = 0;
        foreach (var (id, now) in after)
        {
            if (!before.TryGetValue(id, out var was)) continue;
            down += Math.Max(0, now.Down - was.Down);
            up += Math.Max(0, now.Up - was.Up);
        }
        return (down, up);
    }

    /// <summary>"332 KB/s", "1.2 MB/s", "12 MB/s" (1024-based).</summary>
    public static string FormatRate(double bytesPerSec)
    {
        var kb = bytesPerSec / 1024;
        if (kb < 1024) return string.Create(CultureInfo.InvariantCulture, $"{Math.Round(kb):0} KB/s");
        var mb = kb / 1024;
        return mb < 10
            ? string.Create(CultureInfo.InvariantCulture, $"{mb:0.0} MB/s")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Round(mb):0} MB/s");
    }

    /// <summary>Lit dots in a bar of <paramref name="dots"/> for a 0–100 value; any non-zero value lights at least one.</summary>
    public static int Lit(int percent, int dots) =>
        percent <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(percent / 100.0 * dots), 1, dots);

    /// <summary>A bar turns red above 90%.</summary>
    public static bool Hot(int percent) => percent > 90;
}
