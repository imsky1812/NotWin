namespace NotWin.Core.Wallpapers;

/// <summary>The four Nothing-style wallpaper looks (the bundled ones, <c>wallpapers/make_wallpapers.py</c>).</summary>
public enum WallStyle { HalftoneLight, HalftoneDark, Glyph, Grid }

/// <summary>Everything a generated wallpaper depends on: the look and a seed for every random choice.</summary>
public sealed record WallRecipe(WallStyle Style, int Seed);

/// <summary>One dot: centre and radius in pixels, its grey (0–255), or the single red dot.</summary>
public readonly record struct WallDot(double X, double Y, double R, byte Gray, bool Red);

/// <summary>
/// "✦ New" wallpapers (user, 2026-11-20): the bundled script's four looks — halftone clouds (light / dark), a glyph
/// disc, a minimal dot grid — redrawn with random clouds, density, contrast, disc and red-dot position, at any screen
/// size. Pure: the same recipe and size always give the same dots; exactly one red dot; every dot is on screen.
/// </summary>
public static class WallpaperArt
{
    private const double RefW = 3840, RefH = 2160;

    public static WallRecipe Random(Random rng) =>
        new((WallStyle)rng.Next(Enum.GetValues<WallStyle>().Length), rng.Next());

    /// <summary>The background grey (light looks ≥ 200, dark ≤ 20).</summary>
    public static byte Background(WallStyle style) => style switch
    {
        WallStyle.HalftoneLight => 232,
        WallStyle.HalftoneDark => 10,
        WallStyle.Glyph => 12,
        _ => 238,
    };

    public static IReadOnlyList<WallDot> Dots(WallRecipe recipe, int width, int height)
    {
        var rng = new Random(recipe.Seed);
        var k = Math.Min(width / RefW, height / RefH); // the script's sizes are for a 3840 × 2160 screen
        return recipe.Style switch
        {
            WallStyle.HalftoneLight => Halftone(rng, width, height, k, light: true),
            WallStyle.HalftoneDark => Halftone(rng, width, height, k, light: false),
            WallStyle.Glyph => Glyph(rng, width, height, k),
            _ => Grid(rng, width, height, k),
        };
    }

    // A · B: dots sized by a soft cloud field, plus one red dot on the lattice.
    private static List<WallDot> Halftone(Random rng, int w, int h, double k, bool light)
    {
        var pitch = Math.Max(8, 30 * k);
        var cloud = Cloud(rng);
        var threshold = light ? Between(rng, 0.24, 0.38) : Between(rng, 0.30, 0.42);
        var gamma = light ? Between(rng, 1.4, 1.8) : Between(rng, 1.5, 1.9);
        var strength = light ? 0.85 : 0.9;
        var glowX = Between(rng, 0.3, 0.75) * w;
        var glowY = Between(rng, 0.3, 0.7) * h;
        var glow = light ? 0.25 : 0.0;
        var points = Lattice(w, h, pitch).ToList();
        var raw = points.Select(p => cloud(p.X / w, p.Y / h)).ToList();
        var (min, max) = (raw.Min(), raw.Max());
        var red = points[rng.Next(points.Count)];
        var dots = new List<WallDot>();
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (p == red) continue;
            var f = (raw[i] - min) / Math.Max(1e-9, max - min);
            f = Math.Clamp((f - threshold) / (1 - threshold), 0, 1) * strength;
            var d = Math.Sqrt(Math.Pow(p.X - glowX, 2) + Math.Pow(p.Y - glowY, 2)) / (0.55 * w);
            f = Math.Clamp(f + glow * Math.Clamp(1 - d, 0, 1), 0, 1);
            var r = Math.Pow(f, gamma) * pitch * 0.46;
            if (r < 0.6) continue;
            dots.Add(new WallDot(p.X, p.Y, r, light ? (byte)26 : (byte)236, false));
        }
        dots.Add(new WallDot(red.X, red.Y, pitch * 0.46, 0, true));
        return dots;
    }

    // C: a faint grid, a lit disc of dots shaded from one side, a ring of dots with a red head.
    private static List<WallDot> Glyph(Random rng, int w, int h, double k)
    {
        var pitch = Math.Max(8, 36 * k);
        var ringGap = 70 * k;
        var ringDot = Math.Max(1.5, 7 * k);
        var maxR = Math.Min(w, h) / 2.0 - ringGap - ringDot - 4;
        var radius = Math.Max(pitch * 3, Math.Min(Between(rng, 0.30, 0.40) * h, maxR));
        var margin = radius + ringGap + ringDot + 2;
        var cx = Math.Clamp(Between(rng, 0.3, 0.7) * w, margin, w - margin);
        var cy = Math.Clamp(Between(rng, 0.42, 0.58) * h, margin, h - margin);
        var light = Between(rng, 0, 360);
        var dots = new List<WallDot>();
        foreach (var p in Lattice(w, h, pitch))
        {
            var d = Math.Sqrt(Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2));
            if (d > radius)
            {
                dots.Add(new WallDot(p.X, p.Y, Math.Max(0.8, 2.2 * k), 30, false));
                continue;
            }
            var angle = (Math.Atan2(p.Y - cy, p.X - cx) * 180 / Math.PI + 450) % 360;
            var v = 0.25 + 0.75 * Math.Pow(1 - d / radius, 0.6) * (0.55 + 0.45 * Math.Cos((angle - light) * Math.PI / 180));
            dots.Add(new WallDot(p.X, p.Y, pitch * 0.36, (byte)Math.Clamp(40 + 210 * v, 0, 255), false));
        }
        const int n = 96;
        var head = rng.Next(40, 90);
        for (var i = 0; i < n; i++)
        {
            var a = i / (double)n * 2 * Math.PI - Math.PI / 2;
            var x = cx + (radius + ringGap) * Math.Cos(a);
            var y = cy + (radius + ringGap) * Math.Sin(a);
            dots.Add(new WallDot(x, y, ringDot, i > head ? (byte)60 : (byte)225, i == head));
        }
        return dots;
    }

    // D: a quiet light grid and one red dot.
    private static List<WallDot> Grid(Random rng, int w, int h, double k)
    {
        var pitch = Math.Max(8, 40 * k);
        var points = Lattice(w, h, pitch).ToList();
        var red = points[rng.Next(points.Count)];
        var dots = points.Where(p => p != red).Select(p => new WallDot(p.X, p.Y, Math.Max(0.8, 2.4 * k), 205, false)).ToList();
        dots.Add(new WallDot(red.X, red.Y, Math.Max(3, 14 * k), 0, true));
        return dots;
    }

    private static IEnumerable<(double X, double Y)> Lattice(int w, int h, double pitch)
    {
        for (var y = pitch / 2; y < h; y += pitch)
            for (var x = pitch / 2; x < w; x += pitch)
                yield return (x, y);
    }

    private static double Between(Random rng, double a, double b) => a + rng.NextDouble() * (b - a);

    // Soft clouds: four octaves of smoothly interpolated value noise (u, v in 0..1), coarse lattice first.
    private static Func<double, double, double> Cloud(Random rng)
    {
        var octaves = new List<(int Gw, int Gh, double[] Values, double Amp)>();
        var amp = 1.0;
        for (var o = 0; o < 4; o++)
        {
            int gw = 5 << o, gh = 3 << o;
            var values = new double[(gw + 1) * (gh + 1)];
            for (var i = 0; i < values.Length; i++) values[i] = rng.NextDouble();
            octaves.Add((gw, gh, values, amp));
            amp *= 0.45;
        }
        var total = octaves.Sum(o => o.Amp);
        return (u, v) =>
        {
            var sum = 0.0;
            foreach (var (gw, gh, values, a) in octaves)
            {
                var x = u * gw;
                var y = v * gh;
                int x0 = Math.Min((int)x, gw - 1), y0 = Math.Min((int)y, gh - 1);
                double fx = Smooth(x - x0), fy = Smooth(y - y0);
                double V(int ix, int iy) => values[iy * (gw + 1) + ix];
                var top = V(x0, y0) + (V(x0 + 1, y0) - V(x0, y0)) * fx;
                var bottom = V(x0, y0 + 1) + (V(x0 + 1, y0 + 1) - V(x0, y0 + 1)) * fx;
                sum += a * (top + (bottom - top) * fy);
            }
            return sum / total;
        };
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);
}
