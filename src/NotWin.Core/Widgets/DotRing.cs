namespace NotWin.Core.Widgets;

/// <summary>A ring of dots (Plan 13: stopwatch, sun compass): the first at 12 o'clock, then clockwise.</summary>
public static class DotRing
{
    /// <summary><paramref name="n"/> points on a circle of <paramref name="radius"/> centred on (0, 0); y grows downwards.</summary>
    public static IReadOnlyList<(double X, double Y)> Points(int n, double radius)
    {
        var list = new List<(double, double)>(Math.Max(0, n));
        for (var i = 0; i < n; i++)
        {
            var a = 2 * Math.PI * i / n;
            list.Add((Clean(radius * Math.Sin(a)), Clean(-radius * Math.Cos(a))));
        }
        return list;
    }

    // Sin(π) is 1.2e-16, not 0: round away the float noise so "12 o'clock" is exactly on the axis.
    private static double Clean(double v) => Math.Abs(v) < 1e-12 ? 0 : v;
}
