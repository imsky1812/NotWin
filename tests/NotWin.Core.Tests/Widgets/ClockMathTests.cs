using NotWin.Core.Notch;
using NotWin.Core.Widgets;

namespace NotWin.Core.Tests.Widgets;

public sealed class ClockMathTests
{
    private static TimeSpan H(double h) => TimeSpan.FromHours(h);

    [Fact]
    public void Angles_ThreeFifteen() => Assert.Equal((97.5, 90.0, 0.0), ClockHands.Angles(new TimeOnly(3, 15, 0)));

    [Fact]
    public void Angles_Seconds()
    {
        var (_, minute, second) = ClockHands.Angles(new TimeOnly(0, 0, 30));
        Assert.Equal(3, minute, 9);
        Assert.Equal(180, second, 9);
    }

    [Fact]
    public void GmtLabel()
    {
        Assert.Equal("GMT", ZoneLabels.GmtLabel(TimeSpan.Zero));
        Assert.Equal("GMT+1", ZoneLabels.GmtLabel(H(1)));
        Assert.Equal("GMT−3", ZoneLabels.GmtLabel(H(-3)));
        Assert.Equal("GMT+5:30", ZoneLabels.GmtLabel(H(5.5)));
        Assert.Equal("GMT+5:45", ZoneLabels.GmtLabel(H(5.75)));
        Assert.Equal("GMT−3:30", ZoneLabels.GmtLabel(H(-3.5)));
    }

    [Fact]
    public void Difference()
    {
        Assert.Equal("+4H 30M", ZoneLabels.Difference(H(1), H(5.5)));
        Assert.Equal("SAME TIME", ZoneLabels.Difference(H(2), H(2)));
        Assert.Equal("−1H", ZoneLabels.Difference(H(1), TimeSpan.Zero));
        Assert.Equal("−9H 30M", ZoneLabels.Difference(H(5.5), H(-4)));
    }

    [Fact]
    public void GmtLabel_HalfHourAndDst()
    {
        var adelaide = TimeZoneInfo.FindSystemTimeZoneById("Cen. Australia Standard Time");
        var summer = ZoneLabels.Offset(adelaide, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(H(10.5), summer);
        Assert.Equal("GMT+10:30", ZoneLabels.GmtLabel(summer));
        Assert.Equal(H(9.5), ZoneLabels.Offset(adelaide, new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void DotRing_Points()
    {
        var pts = DotRing.Points(4, 10);
        (double, double)[] expected = [(0, -10), (10, 0), (0, 10), (-10, 0)];
        Assert.Equal(4, pts.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(expected[i].Item1, pts[i].X, 9);
            Assert.Equal(expected[i].Item2, pts[i].Y, 9);
        }
    }

    [Fact]
    public void DotFont_HasHAndPlus()
    {
        Assert.Equal(DotFont.ColumnsFor("6020"), DotFont.ColumnsFor("6H20"));
        Assert.NotNull(DotFont.GlyphRows('+'));
        Assert.NotEmpty(DotFont.LitDots("H"));
    }
}
