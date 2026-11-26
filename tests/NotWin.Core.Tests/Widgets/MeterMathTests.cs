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
