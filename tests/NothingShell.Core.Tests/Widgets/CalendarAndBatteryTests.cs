using NothingShell.Core.Widgets;

namespace NothingShell.Core.Tests.Widgets;

public sealed class CalendarMonthTests
{
    [Fact]
    public void November2026_StartsSunday1_Has35Cells_TodayMarked()
    {
        var cells = CalendarMonth.Build(2026, 11, new DateOnly(2026, 11, 19));
        Assert.Equal(35, cells.Count);
        Assert.Equal(new CalendarCell(1, true, false), cells[0]);           // 1 Nov 2026 is a Sunday
        Assert.Equal(new CalendarCell(19, true, true), cells[18]);
        Assert.Equal(new CalendarCell(30, true, false), cells[29]);
        Assert.Equal(new CalendarCell(1, false, false), cells[30]);         // December, muted
        Assert.Single(cells, c => c.IsToday);
    }

    [Fact]
    public void April2026_LeadingDaysMuted()
    {
        var cells = CalendarMonth.Build(2026, 4, new DateOnly(2026, 11, 19));
        Assert.Equal(new CalendarCell(29, false, false), cells[0]);         // 29-31 March
        Assert.Equal(new CalendarCell(31, false, false), cells[2]);
        Assert.Equal(new CalendarCell(1, true, false), cells[3]);           // 1 April 2026 is a Wednesday
        Assert.Equal(35, cells.Count);
        Assert.DoesNotContain(cells, c => c.IsToday);
    }
}

public sealed class BatteryPagesTests
{
    private static readonly BatteryDevice Laptop = new("laptop", "Laptop", "laptop", 85, false, true);
    private static readonly BatteryDevice Buds = new("bt1", "CMF Buds 2", "head", 60, false, false);
    private static readonly BatteryDevice Mouse = new("bt2", "Mouse", "mouse", null, false, false);

    [Fact]
    public void BatteryPages_NewDevice_JumpsToIt()
    {
        var p = new BatteryPages();
        p.Update([Laptop]);
        Assert.Equal(0, p.Current);
        p.Update([Laptop, Buds]);
        Assert.Equal("bt1", p.Devices[p.Current].Id);
    }

    [Fact]
    public void BatteryPages_CurrentVanishes_BackToLaptop()
    {
        var p = new BatteryPages();
        p.Update([Laptop]);
        p.Update([Laptop, Buds]);
        p.Update([Laptop]);
        Assert.Equal(0, p.Current);
        Assert.True(p.Devices[0].IsLaptop);
    }

    [Fact]
    public void BatteryPages_LaptopFirst_ThenByName()
    {
        var p = new BatteryPages();
        p.Update([Mouse, Buds, Laptop]);
        Assert.Equal(["laptop", "bt1", "bt2"], p.Devices.Select(d => d.Id));
    }

    [Fact]
    public void BatteryPages_Scroll_Clamped()
    {
        var p = new BatteryPages();
        p.Update([Laptop]);
        p.Update([Laptop, Buds]);
        p.Go(0);
        p.Scroll(1);
        Assert.Equal(1, p.Current);
        p.Scroll(1);
        Assert.Equal(1, p.Current);
        p.Scroll(-5);
        Assert.Equal(0, p.Current);
        p.Go(9);
        Assert.Equal(1, p.Current);
    }

    [Fact]
    public void BatteryPages_NoPercent_KeptWithoutLevel()
    {
        var p = new BatteryPages();
        p.Update([Laptop, Mouse]);
        Assert.Contains(p.Devices, d => d.Id == "bt2" && d.Percent is null);
    }

    [Fact]
    public void ClockInk_FlipsAtMidGrey()
    {
        Assert.False(ClockInk.For(0.49));
        Assert.True(ClockInk.For(0.5));
    }
}
