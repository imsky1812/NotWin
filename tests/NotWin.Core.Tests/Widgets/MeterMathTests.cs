using NotWin.Core.Widgets;

namespace NotWin.Core.Tests.Widgets;

public sealed class MeterMathTests
{
    [Fact]
    public void Cpu_Half() => Assert.Equal(50, MeterMath.CpuPercent((1000, 2000, 3000), (1050, 2060, 3040)));

    [Fact]
    public void Cpu_NoTime_IsZero() => Assert.Equal(0, MeterMath.CpuPercent((5, 5, 5), (5, 5, 5)));

    [Fact]
    public void Cpu_ClampedTo100() => Assert.Equal(100, MeterMath.CpuPercent((0, 0, 0), (0, 10, 200)));

    [Fact]
    public void Cpu_CounterWentBack_IsZero() => Assert.Equal(0, MeterMath.CpuPercent((100, 100, 100), (50, 60, 70)));

    [Fact]
    public void NetRate_PerSecond() => Assert.Equal(1e6, MeterMath.NetRate(5_000_000, 7_000_000, TimeSpan.FromSeconds(2)));

    [Fact]
    public void NetRate_CounterWentBack_IsZero() => Assert.Equal(0, MeterMath.NetRate(7_000_000, 1_000, TimeSpan.FromSeconds(2)));

    [Fact]
    public void NetRate_NoTime_IsZero() => Assert.Equal(0, MeterMath.NetRate(0, 100, TimeSpan.Zero));

    [Fact]
    public void FormatRate()
    {
        Assert.Equal("0 KB/s", MeterMath.FormatRate(0));
        Assert.Equal("332 KB/s", MeterMath.FormatRate(340_000));
        Assert.Equal("1.2 MB/s", MeterMath.FormatRate(1_258_291));
        Assert.Equal("12 MB/s", MeterMath.FormatRate(12.4 * 1024 * 1024));
    }

    [Fact]
    public void Lit()
    {
        Assert.Equal(0, MeterMath.Lit(0, 10));
        Assert.Equal(1, MeterMath.Lit(5, 10));
        Assert.Equal(1, MeterMath.Lit(1, 10));
        Assert.Equal(10, MeterMath.Lit(100, 10));
        Assert.Equal(10, MeterMath.Lit(140, 10));
    }

    [Fact]
    public void Hot()
    {
        Assert.True(MeterMath.Hot(91));
        Assert.False(MeterMath.Hot(90));
    }
}

// Plan 13 review: per-interface deltas, so an adapter coming up (with its lifetime counters) is not a spike.
public sealed class NetDeltaTests
{
    private static Dictionary<string, (long Down, long Up)> D(params (string Id, long Down, long Up)[] xs) =>
        xs.ToDictionary(x => x.Id, x => (x.Down, x.Up));

    [Fact]
    public void NetDelta_SumsInterfacesInBoth() =>
        Assert.Equal((300L, 30L), MeterMath.NetDelta(D(("a", 100, 10), ("b", 1000, 100)), D(("a", 200, 20), ("b", 1200, 120))));

    [Fact]
    public void NetDelta_NewInterface_Ignored() =>
        Assert.Equal((100L, 10L), MeterMath.NetDelta(D(("a", 100, 10)), D(("a", 200, 20), ("vpn", 9_000_000_000, 4_000_000_000))));

    [Fact]
    public void NetDelta_InterfaceWentBack_CountsZero() =>
        Assert.Equal((50L, 0L), MeterMath.NetDelta(D(("a", 100, 10), ("b", 5000, 500)), D(("a", 150, 5), ("b", 10, 1))));

    [Fact]
    public void NetDelta_InterfaceGone_Ignored() =>
        Assert.Equal((0L, 0L), MeterMath.NetDelta(D(("a", 100, 10)), D()));
}
