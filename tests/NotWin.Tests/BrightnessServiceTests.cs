using NotWin.Panel;

namespace NotWin.Tests;

public sealed class BrightnessServiceTests
{
    private sealed class FakeBackend : IBrightnessBackend
    {
        public int? Level { get; set; } = 50;
        public List<int> Writes { get; } = [];
        public int? Read() => Level;
        public void Write(int percent) { Writes.Add(percent); Level = percent; }
    }

    private sealed class Clock
    {
        public DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
        public readonly List<(TimeSpan Delay, Action Run)> Scheduled = [];
    }

    private static (BrightnessService, FakeBackend, Clock) Make(int? level = 50)
    {
        var b = new FakeBackend { Level = level };
        var c = new Clock();
        var s = new BrightnessService(b, () => c.Now, (d, a) => c.Scheduled.Add((d, a)), runInline: true);
        return (s, b, c);
    }

    [Fact]
    public async Task Unsupported_WhenBackendReadsNull()
    {
        var (s, _, _) = Make(level: null);
        Assert.Null(await s.GetAsync());
    }

    [Fact]
    public async Task Get_ReturnsBackendLevel()
    {
        var (s, _, _) = Make(level: 70);
        Assert.Equal(70, await s.GetAsync());
    }

    [Fact]
    public void Set_ClampsTo0_100()
    {
        var (s, b, c) = Make();
        s.Set(150);
        c.Now = c.Now.AddSeconds(1);
        s.Set(-5);
        Assert.Equal([100, 0], b.Writes);
    }

    [Fact]
    public void Set_Coalesces_RapidDrags()
    {
        var (s, b, c) = Make();
        s.Set(10);                               // written at once
        c.Now = c.Now.AddMilliseconds(10); s.Set(20);
        c.Now = c.Now.AddMilliseconds(10); s.Set(30); // still inside 50 ms: only the last pending value matters
        Assert.Equal([10], b.Writes);
        Assert.Single(c.Scheduled);
        c.Now = c.Now.AddMilliseconds(40);
        c.Scheduled[0].Run();
        Assert.Equal([10, 30], b.Writes);        // the final value is always written
    }
}
