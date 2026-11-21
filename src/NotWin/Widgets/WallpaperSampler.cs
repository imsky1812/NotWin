using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotWin.Core;

namespace NotWin.Widgets;

/// <summary>
/// Mean brightness of the wallpaper under a screen rectangle, so the clock can pick dark or light dots (spec §6.5).
/// Decodes the wallpaper once at 256 px wide (cached per file version: Windows rewrites its wallpaper file in place,
/// so the path alone is not enough) and maps the rectangle as Windows' "Fill" style does:
/// scaled to cover the monitor, centred.
/// </summary>
public static class WallpaperSampler
{
    private static (string Path, DateTime Written, long Length)? s_key;
    private static byte[]? s_pixels;
    private static int s_w, s_h;

    /// <summary>Relative luminance 0..1, or null when there is no readable wallpaper (a solid colour, a slideshow…).</summary>
    public static double? MeanLuminance(string? wallpaperPath, RectI screen, RectI monitor)
    {
        if (string.IsNullOrEmpty(wallpaperPath) || !File.Exists(wallpaperPath)) return null;
        if (!Load(wallpaperPath)) return null;
        double monW = monitor.Right - monitor.Left, monH = monitor.Bottom - monitor.Top;
        var cover = Math.Max(monW / s_w, monH / s_h);         // image px -> screen px
        var offX = (s_w * cover - monW) / 2;
        var offY = (s_h * cover - monH) / 2;
        int x0 = Clamp((screen.Left - monitor.Left + offX) / cover, s_w), x1 = Clamp((screen.Right - monitor.Left + offX) / cover, s_w);
        int y0 = Clamp((screen.Top - monitor.Top + offY) / cover, s_h), y1 = Clamp((screen.Bottom - monitor.Top + offY) / cover, s_h);
        if (x1 <= x0 || y1 <= y0) return null;
        double sum = 0;
        long n = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var i = (y * s_w + x) * 4;
                sum += (0.0722 * s_pixels![i] + 0.7152 * s_pixels[i + 1] + 0.2126 * s_pixels[i + 2]) / 255; // BGRA
                n++;
            }
        return n == 0 ? null : sum / n;
    }

    private static int Clamp(double v, int max) => Math.Clamp((int)v, 0, max);

    private static bool Load(string path)
    {
        (string, DateTime, long) key;
        try
        {
            var info = new FileInfo(path);
            key = (path, info.LastWriteTimeUtc, info.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        if (s_key == key && s_pixels is not null) return true;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // WPF's own cache would hand back the old picture
            img.DecodePixelWidth = 256;
            img.UriSource = new Uri(path);
            img.EndInit();
            var bgra = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
            s_w = bgra.PixelWidth;
            s_h = bgra.PixelHeight;
            s_pixels = new byte[s_w * s_h * 4];
            bgra.CopyPixels(s_pixels, s_w * 4, 0);
            s_key = key;
            return true;
        }
        catch (Exception e)
        {
            Log.Error("reading the wallpaper for the clock failed", e);
            s_key = null;
            s_pixels = null;
            return false;
        }
    }
}
