using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NothingShell.Core.Wallpapers;

namespace NothingShell.Settings;

/// <summary>
/// Draws a "✦ New" wallpaper (<see cref="WallpaperArt"/>) at the screen's size and saves it as
/// <c>nothing_new_*.png</c> with a 1280 px <c>_preview.jpg</c> in the user's wallpaper folder. Only the newest
/// <see cref="Keep"/> are kept (plus the one in use). Call on a WPF (STA) thread.
/// </summary>
public static class WallpaperMaker
{
    public const int Keep = 12;
    private const string Prefix = "nothing_new_";
    private static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x23, 0x1F)));

    /// <summary>Renders and saves; returns the PNG's path.</summary>
    public static string Make(WallRecipe recipe, int width, int height, string dir)
    {
        Directory.CreateDirectory(dir);
        var visual = new DrawingVisual();
        var grays = new Dictionary<byte, Brush>();
        Brush Gray(byte g) => grays.TryGetValue(g, out var b) ? b : grays[g] = Freeze(new SolidColorBrush(Color.FromRgb(g, g, g)));
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Gray(WallpaperArt.Background(recipe.Style)), null, new Rect(0, 0, width, height));
            foreach (var d in WallpaperArt.Dots(recipe, width, height))
                dc.DrawEllipse(d.Red ? Red : Gray(d.Gray), null, new Point(d.X, d.Y), d.R, d.R);
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var name = $"{Prefix}{DateTime.Now:yyyyMMdd_HHmmss}_{recipe.Seed & 0xFFFF:x4}";
        var png = Path.Combine(dir, name + ".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var f = File.Create(png)) encoder.Save(f);

        var scale = 1280.0 / width;
        var preview = new JpegBitmapEncoder { QualityLevel = 86 };
        preview.Frames.Add(BitmapFrame.Create(new TransformedBitmap(bitmap, new ScaleTransform(scale, scale))));
        using (var f = File.Create(PreviewOf(png))) preview.Save(f);
        return png;
    }

    public static string PreviewOf(string png) => Path.ChangeExtension(png, null) + "_preview.jpg";

    /// <summary>The generated wallpapers in <paramref name="dir"/>, newest first.</summary>
    public static IReadOnlyList<string> List(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        return new DirectoryInfo(dir).GetFiles(Prefix + "*.png")
            .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => f.FullName).ToList();
    }

    /// <summary>Deletes all but the newest <see cref="Keep"/> (never <paramref name="keep"/>, the one in use), with their previews.</summary>
    public static void Prune(string dir, string? keep)
    {
        foreach (var old in List(dir).Skip(Keep))
        {
            if (keep is not null && string.Equals(old, keep, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                File.Delete(old);
                File.Delete(PreviewOf(old));
            }
            catch (IOException) { } // in use somewhere: try again next time
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Deletes a wallpaper the user made, with its preview (user, 2026-11-20). Only a <c>nothing_new_*.png</c> directly
    /// inside <paramref name="dir"/>; anything else is refused (false).
    /// </summary>
    public static bool Delete(string dir, string png)
    {
        try
        {
            var full = Path.GetFullPath(png);
            var folder = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(full), folder, StringComparison.OrdinalIgnoreCase)) return false;
            var name = Path.GetFileName(full);
            if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
            File.Delete(full);
            File.Delete(PreviewOf(full));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}
