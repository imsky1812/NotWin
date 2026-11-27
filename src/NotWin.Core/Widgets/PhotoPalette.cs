namespace NotWin.Core.Widgets;

/// <summary>The soft Lucent wallpaper's colours: a background and three blob colours.</summary>
public sealed record LucentColours((byte R, byte G, byte B) Base, IReadOnlyList<(byte R, byte G, byte B)> Blobs);

/// <summary>
/// Colours for the Lucent wallpaper from the photo frame's photo (Plan 13 follow-up, user 2026-11-26): the three
/// strongest hues of a small decoded copy (near-black, near-white and grey pixels left out), then softened — pastel on
/// light grey for light mode, deep on charcoal for dark mode — so the wallpaper never fights the widgets.
/// </summary>
public static class PhotoPalette
{
    private const int Bins = 12;

    /// <summary>Up to three colours, strongest first; a photo without colour gives greys.</summary>
    public static IReadOnlyList<(byte R, byte G, byte B)> Extract(ReadOnlySpan<byte> bgra, int width, int height)
    {
        var sum = new double[Bins, 4]; // R, G, B, weight
        double nr = 0, ng = 0, nb = 0, nw = 0;
        var count = Math.Min(bgra.Length / 4, Math.Max(0, width * height));
        for (var i = 0; i < count; i++)
        {
            int b = bgra[i * 4], g = bgra[i * 4 + 1], r = bgra[i * 4 + 2];
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max < 24 || min > 236) continue; // near black / white say nothing about the photo's colour
            var sat = (max - min) / (double)max;
            if (sat < 0.18)
            {
                nr += r; ng += g; nb += b; nw++;
                continue;
            }
            var bin = (int)(Hue(r, g, b, max, min) / 360.0 * Bins) % Bins;
            sum[bin, 0] += r * sat; sum[bin, 1] += g * sat; sum[bin, 2] += b * sat; sum[bin, 3] += sat;
        }
        var total = 0.0;
        for (var k = 0; k < Bins; k++) total += sum[k, 3];
        var picks = Enumerable.Range(0, Bins)
            .Where(k => sum[k, 3] > 0 && sum[k, 3] >= total * 0.02)
            .OrderByDescending(k => sum[k, 3])
            .Take(3)
            .Select(k => (Byte(sum[k, 0] / sum[k, 3]), Byte(sum[k, 1] / sum[k, 3]), Byte(sum[k, 2] / sum[k, 3])))
            .ToList();
        var grey = nw > 0 ? (Byte(nr / nw), Byte(ng / nw), Byte(nb / nw)) : ((byte)128, (byte)128, (byte)128);
        var shade = 0;
        while (picks.Count < 3)
        {
            // Not enough colour: greys around the photo's neutral tone.
            var d = shade++ switch { 0 => 0, 1 => 40, _ => -40 };
            picks.Add((Byte(grey.Item1 + d), Byte(grey.Item2 + d), Byte(grey.Item3 + d)));
        }
        return picks;
    }

    /// <summary>The wallpaper's background and blobs: pastel on light grey, or deep on charcoal.</summary>
    public static LucentColours Lucent(IReadOnlyList<(byte R, byte G, byte B)> colours, bool light)
    {
        (byte, byte, byte) Mix((byte R, byte G, byte B) c, (byte R, byte G, byte B) to, double towards) =>
            (Byte(c.R + (to.R - c.R) * towards), Byte(c.G + (to.G - c.G) * towards), Byte(c.B + (to.B - c.B) * towards));
        return light
            ? new LucentColours((226, 226, 228), colours.Select(c => Mix(c, (255, 255, 255), 0.66)).ToList())
            : new LucentColours((18, 18, 20), colours.Select(c => Mix(c, (14, 14, 16), 0.70)).ToList());
    }

    private static double Hue(int r, int g, int b, int max, int min)
    {
        double d = max - min;
        var h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        return h < 0 ? h + 360 : h;
    }

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}

/// <summary>
/// Which wallpapers the photo tint may replace (Plan 13 follow-up): only NotWin's own Lucent wallpapers — the bundled
/// <c>nothing_lucent_*</c> and the tinted <c>lucent_tint_*</c> it made itself. A wallpaper the user picked is never touched.
/// </summary>
public static class LucentWallpaper
{
    public static bool Owns(string? currentPath)
    {
        if (string.IsNullOrEmpty(currentPath)) return false;
        var name = Path.GetFileName(currentPath);
        return name.StartsWith("nothing_lucent_", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("lucent_tint_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The tinted file to write next: <c>lucent_tint_{light|dark}_{a|b}.png</c>, never the name now on screen (Windows
    /// keeps showing a cached copy when the same path is set again).
    /// </summary>
    public static string FileName(bool light, string? current)
    {
        var showingA = current is not null && Path.GetFileNameWithoutExtension(current).EndsWith("_a", StringComparison.OrdinalIgnoreCase)
                       && Owns(current);
        return $"lucent_tint_{(light ? "light" : "dark")}_{(showingA ? "b" : "a")}.png";
    }
}
