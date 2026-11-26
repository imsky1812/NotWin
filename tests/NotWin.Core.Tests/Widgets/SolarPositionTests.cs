using NotWin.Core.Widgets;

namespace NotWin.Core.Tests.Widgets;

/// <summary>Expected values worked from the NOAA solar equations (solar noon, declination, 90.833° zenith).</summary>
public sealed class SolarPositionTests
{
    private static DateTime Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void Position_London_Noon()
    {
        var (az, el) = SolarPosition.Position(Utc(2026, 6, 21, 12), 51.5074, -0.1278);
        Assert.InRange(el, 60.9, 62.9);
        Assert.InRange(az, 178, 180);
    }

    [Fact]
    public void Position_Morning_IsEast_Evening_IsWest()
    {
        Assert.InRange(SolarPosition.Position(Utc(2026, 6, 21, 7), 51.5074, -0.1278).Azimuth, 60, 120);
        Assert.InRange(SolarPosition.Position(Utc(2026, 6, 21, 17), 51.5074, -0.1278).Azimuth, 240, 300);
    }

    [Fact]
    public void Position_Sydney() =>
        Assert.InRange(SolarPosition.Position(Utc(2026, 12, 21, 2), -33.8688, 151.2093).Elevation, 78.5, 80.5);

    [Fact]
    public void Position_Midnight_BelowHorizon() =>
        Assert.True(SolarPosition.Position(Utc(2026, 12, 21, 0), 51.5074, -0.1278).Elevation < -50);

    [Fact]
    public void SunTimes_Delhi()
    {
        var t = SolarPosition.SunTimes(new DateOnly(2026, 3, 20), 28.6139, 77.2090, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
        Assert.NotNull(t);
        Assert.InRange(t.Value.Rise, new TimeOnly(6, 23), new TimeOnly(6, 27));
        Assert.InRange(t.Value.Set, new TimeOnly(18, 30), new TimeOnly(18, 34));
    }

    [Fact]
    public void SunTimes_PolarNight_IsNone() =>
        Assert.Null(SolarPosition.SunTimes(new DateOnly(2026, 12, 21), 78.22, 15.65, TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time")));

    [Fact]
    public void SunTimes_PolarDay_IsNone() =>
        Assert.Null(SolarPosition.SunTimes(new DateOnly(2026, 6, 21), 78.22, 15.65, TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time")));

    [Fact]
    public void Position_DateLine()
    {
        var east = SolarPosition.Position(Utc(2026, 3, 20, 0), -17, 179.9).Elevation;
        var west = SolarPosition.Position(Utc(2026, 3, 20, 0), -17, -179.9).Elevation;
        Assert.True(Math.Abs(east - west) < 0.5, $"{east} vs {west}");
    }

    [Fact]
    public void SunTimes_DateLine_Fiji()
    {
        var t = SolarPosition.SunTimes(new DateOnly(2026, 3, 20), -18.1416, 178.4419, TimeZoneInfo.FindSystemTimeZoneById("Fiji Standard Time"));
        Assert.NotNull(t);
        Assert.InRange(t.Value.Rise, new TimeOnly(5, 30), new TimeOnly(6, 45));
        Assert.InRange(t.Value.Set, new TimeOnly(17, 45), new TimeOnly(19, 0));
    }
}
