namespace NothingShell.Core.Timing;

/// <summary>
/// The grid timer (mockup <c>gridTimer</c> / <c>scatter</c>): n×n dots go out in a fixed scattered order; the next to go
/// out is drawn red while running.
/// </summary>
public static class TimerGrid
{
    private static readonly Dictionary<int, int[]> Cache = [];

    /// <summary>The mockup's scatter(N): Fisher-Yates from the end on a small LCG seeded with 7.</summary>
    public static IReadOnlyList<int> Order(int n)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(n, out var cached)) return cached;
            var a = Enumerable.Range(0, n * n).ToArray();
            long s = 7;
            for (var i = a.Length - 1; i > 0; i--)
            {
                s = (s * 9301 + 49297) % 233280;
                var j = (int)Math.Floor(s / 233280.0 * (i + 1));
                (a[i], a[j]) = (a[j], a[i]);
            }
            Cache[n] = a;
            return a;
        }
    }

    /// <summary>Dots still lit for <paramref name="fraction"/> remaining, and the next one to go out (null when none).</summary>
    public static (IReadOnlySet<int> Alive, int? Next) State(int n, double fraction)
    {
        var order = Order(n);
        var total = n * n;
        var lit = Math.Clamp((int)Math.Ceiling(Math.Clamp(fraction, 0, 1) * total), 0, total);
        var gone = total - lit;
        var alive = order.Skip(gone).ToHashSet();
        int? next = gone > 0 && lit > 0 ? order[gone] : null;
        return (alive, next);
    }
}
