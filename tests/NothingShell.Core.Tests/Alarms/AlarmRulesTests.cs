using NothingShell.Core.Alarms;

namespace NothingShell.Core.Tests.Alarms;

// Plan 9: Nothing Shell's own alarms. Days: bit 0 = Sunday … bit 6 = Saturday; 0 = once.
public sealed class AlarmRulesTests
{
    private const int Weekdays = 0b0111110, EveryDay = 0b1111111, Weekends = 0b1000001;
    private static Alarm A(int h, int m, int days = 0, bool on = true) => new("a", h, m, days, on);
    private static DateTime Sat(int h, int m = 0) => new(2026, 11, 21, h, m, 0); // 2026-11-21 is a Saturday

    [Fact]
    public void NextRing_OnceToday_ThenTomorrow()
    {
        Assert.Equal(Sat(9, 30), AlarmRules.NextRing(A(9, 30), Sat(8)));
        Assert.Equal(Sat(9, 30).AddDays(1), AlarmRules.NextRing(A(9, 30), Sat(10)));
        Assert.Equal(Sat(9, 30).AddDays(1), AlarmRules.NextRing(A(9, 30), Sat(9, 30))); // strictly after
    }

    [Fact]
    public void NextRing_Weekdays_FridayNightGoesToMonday()
    {
        var fridayNight = new DateTime(2026, 11, 20, 23, 0, 0);
        Assert.Equal(new DateTime(2026, 11, 23, 7, 0, 0), AlarmRules.NextRing(A(7, 0, Weekdays), fridayNight));
        Assert.Equal(new DateTime(2026, 11, 22, 7, 0, 0), AlarmRules.NextRing(A(7, 0, Weekends), Sat(8)));
        Assert.Equal(Sat(23, 59), AlarmRules.NextRing(A(23, 59, EveryDay), Sat(23, 58)));
        Assert.Equal(Sat(0, 0).AddDays(1), AlarmRules.NextRing(A(0, 0, EveryDay), Sat(23, 59)));
    }

    [Fact]
    public void NextRing_Off_IsNull() => Assert.Null(AlarmRules.NextRing(A(7, 0, on: false), Sat(6)));

    [Fact]
    public void Next_SoonestOfAll()
    {
        var all = new[] { A(9, 0) with { Id = "late" }, A(8, 0) with { Id = "early" }, A(7, 0, on: false) with { Id = "off" } };
        var next = AlarmRules.Next(all, Sat(6));
        Assert.Equal(("early", Sat(8)), (next!.Value.Alarm.Id, next.Value.At));
        Assert.Null(AlarmRules.Next([], Sat(6)));
    }

    [Fact]
    public void Due_FiresOnceInTheWindow()
    {
        var all = new[] { A(7, 0) };
        Assert.Single(AlarmRules.Due(all, Sat(6, 59), Sat(7, 0)));
        Assert.Empty(AlarmRules.Due(all, Sat(7, 0), Sat(7, 1)));   // already rung
        Assert.Empty(AlarmRules.Due(all, Sat(6, 0), Sat(6, 59)));  // not yet
    }

    [Theory]
    [InlineData(0, "ONCE")]
    [InlineData(EveryDay, "EVERY DAY")]
    [InlineData(Weekdays, "WEEKDAYS")]
    [InlineData(Weekends, "WEEKENDS")]
    [InlineData(0b0101010, "MON WED FRI")]
    public void DaysLabel_Names(int days, string label) => Assert.Equal(label, AlarmRules.DaysLabel(days));

    [Theory]
    [InlineData(25, "IN 25 MIN")]
    [InlineData(0.5, "IN 1 MIN")]
    [InlineData(8 * 60 + 10, "IN 8 H")]
    [InlineData(50 * 60, "IN 2 D")]
    public void Until_Units(double minutes, string label) =>
        Assert.Equal(label, AlarmRules.Until(Sat(6).AddMinutes(minutes), Sat(6)));

    [Theory]
    [InlineData("7:30", 7, 30)]
    [InlineData("07:30", 7, 30)]
    [InlineData("730", 7, 30)]
    [InlineData("0000", 0, 0)]
    [InlineData(" 23:59 ", 23, 59)]
    public void ParseTime_Good(string text, int h, int m) => Assert.Equal((h, m), AlarmRules.ParseTime(text));

    [Theory]
    [InlineData("25:00")]
    [InlineData("7")]
    [InlineData("abc")]
    [InlineData("7:60")]
    [InlineData("")]
    public void ParseTime_Bad(string text) => Assert.Null(AlarmRules.ParseTime(text));

    [Fact]
    public void Store_RoundTrip_AndBadFileIsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ns-alarms-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AlarmStore(dir);
            Assert.Empty(store.Load());
            store.Save([A(7, 0, Weekdays), A(9, 15) with { Id = "b", On = false }]);
            Assert.Equal([A(7, 0, Weekdays), A(9, 15) with { Id = "b", On = false }], new AlarmStore(dir).Load());
            File.WriteAllText(Path.Combine(dir, "alarms.json"), "{ not json");
            Assert.Empty(new AlarmStore(dir).Load());
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
