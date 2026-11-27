using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotWin.Widgets;

/// <summary>
/// An app icon in greys (Plan 13 follow-up, user 2026-11-27: folder icons like Nothing OS, where the apps inside show as
/// monochrome icons). Alpha is kept; the result is frozen and cached per source image.
/// </summary>
public static class MonoIcon
{
    private static readonly ConditionalWeakTable<ImageSource, ImageSource> Cache = [];

    public static ImageSource? From(ImageSource? source)
    {
        if (source is not BitmapSource bitmap) return source;
        if (Cache.TryGetValue(source, out var cached)) return cached;
        try
        {
            var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var w = bgra.PixelWidth;
            var h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);
            for (var i = 0; i < px.Length; i += 4)
            {
                var y = (byte)Math.Clamp(Math.Round(0.114 * px[i] + 0.587 * px[i + 1] + 0.299 * px[i + 2]), 0, 255);
                px[i] = px[i + 1] = px[i + 2] = y;
            }
            var mono = BitmapSource.Create(w, h, bitmap.DpiX, bitmap.DpiY, PixelFormats.Bgra32, null, px, w * 4);
            mono.Freeze();
            Cache.AddOrUpdate(source, mono);
            return mono;
        }
        catch (Exception)
        {
            return source; // an icon that can't be read stays as it is
        }
    }
}
