using System.IO;
using NothingShell.Alarms;
using NothingShell.Core.Alarms;

namespace NothingShell.Tests;

// Plan 9: the alarm service rings each alarm once at its minute, turns "once" alarms off, snoozes for 5 minutes.
public sealed class AlarmServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-alarmsvc-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 21, 6, 59, 0);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private AlarmService Service(params Alarm[] alarms)
    {
        var store = new AlarmStore(_dir);
        store.Save(alarms);
        return new AlarmService(store, () => _now);
    }

    [Fact]
    public void Check_RingsOnceAtItsMinute()
    {
        var s = Service(new Alarm("a", 7, 0, 0b1111111, true));
        var rings = 0;
        s.Ringing += _ => rings++;
        s.Check();
        Assert.Equal(0, rings);
        _now = _now.AddMinutes(1);
        s.Check();
        s.Check();
        Assert.Equal(1, rings);
        Assert.Equal("a", s.RingingNow?.Id);
    }

    [Fact]
    public void OnceAlarm_TurnsOffAfterRinging()
    {
        var s = Service(new Alarm("a", 7, 0, 0, true));
        _now = _now.AddMinutes(1);
        s.Check();
        Assert.False(s.Alarms.Single().On);
        Assert.False(new AlarmStore(_dir).Load().Single().On);
    }

    [Fact]
    public void Snooze_RingsAgainInFiveMinutes()
    {
        var s = Service(new Alarm("a", 7, 0, 0b1111111, true));
        var rings = 0;
        s.Ringing += _ => rings++;
        _now = _now.AddMinutes(1);
        s.Check();
        s.Snooze();
        Assert.Null(s.RingingNow);
        _now = _now.AddMinutes(4);
        s.Check();
        Assert.Equal(1, rings);
        _now = _now.AddMinutes(1);
        s.Check();
        Assert.Equal(2, rings);
    }

    [Fact]
    public void Stop_ClearsRinging()
    {
        var s = Service(new Alarm("a", 7, 0, 0b1111111, true));
        var quiet = 0;
        s.Quiet += () => quiet++;
        _now = _now.AddMinutes(1);
        s.Check();
        s.Stop();
        Assert.Null(s.RingingNow);
        Assert.Equal(1, quiet);
    }

    [Fact]
    public void Set_SavesAndTells()
    {
        var s = Service();
        var changed = 0;
        s.Changed += () => changed++;
        s.Set([new Alarm("b", 8, 0, 0, true)]);
        Assert.Equal(1, changed);
        Assert.Equal("b", new AlarmStore(_dir).Load().Single().Id);
    }
}
