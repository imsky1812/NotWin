using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotWin.Widgets;
using static NotWin.Tests.WidgetSurfaceTests;

namespace NotWin.Tests;

/// <summary>Plan 13 follow-up: the Lucent wallpaper takes its colours from the photo frame. The wallpaper is a fake.</summary>
public sealed class PhotoTintTests
{
    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "notwin-tint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    // A small solid-colour PNG standing in for a frame photo.
    private static string Photo(string dir, Color c)
    {
        var px = new byte[32 * 32 * 4];
        for (var i = 0; i < px.Length; i += 4) { px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255; }
        var bmp = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, px, 32 * 4);
        var path = Path.Combine(dir, "photo.png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
        return path;
    }

    private static Color CentrePixel(string png)
    {
        var source = new BitmapImage();
        source.BeginInit();
        source.UriSource = new Uri(png);
        source.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file open (the temp folder is deleted after)
        source.EndInit();
        var img = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        img.CopyPixels(new System.Windows.Int32Rect(img.PixelWidth / 5, img.PixelHeight / 4, 1, 1), px, 4, 0);
        return Color.FromRgb(px[2], px[1], px[0]);
    }

    [Fact]
    public void Tints_NotWinsLucentWallpaper_FromThePhoto() => OnSta(() =>
    {
        var dir = TempDir();
        try
        {
            string? current = @"C:\Program Files\NotWin\Wallpapers\nothing_lucent_light.png";
            var applied = new List<string>();
            using var tint = new PhotoTint(dir, () => current, p => { applied.Add(p); current = p; }, () => true);
            tint.OnPhoto(Photo(dir, Color.FromRgb(210, 20, 25)));
            tint.RunNow();
            var made = Assert.Single(applied);
            Assert.Equal("lucent_tint_light_a.png", Path.GetFileName(made));
            Assert.True(File.Exists(made));
            var c = CentrePixel(made);
            Assert.True(c.R > c.G && c.R > c.B, $"reddish: {c}");
            Assert.True(c.G > 150, $"pastel in light mode: {c}");

            tint.RunNow(); // next slide: the other file, so Windows reloads it
            Assert.Equal("lucent_tint_light_b.png", Path.GetFileName(applied[1]));
        }
        finally { Directory.Delete(dir, true); }
    });

    [Fact]
    public void Dark_IsDeep() => OnSta(() =>
    {
        var dir = TempDir();
        try
        {
            string? current = @"C:\x\nothing_lucent_dark.png";
            var applied = new List<string>();
            using var tint = new PhotoTint(dir, () => current, p => { applied.Add(p); current = p; }, () => false);
            tint.OnPhoto(Photo(dir, Color.FromRgb(40, 110, 220)));
            tint.RunNow();
            var c = CentrePixel(Assert.Single(applied));
            Assert.True(Math.Max(c.R, Math.Max(c.G, c.B)) < 100, $"deep in dark mode: {c}");
        }
        finally { Directory.Delete(dir, true); }
    });

    [Fact]
    public void NeverReplaces_AWallpaperTheUserPicked() => OnSta(() =>
    {
        var dir = TempDir();
        try
        {
            var applied = new List<string>();
            using var tint = new PhotoTint(dir, () => @"C:\Users\me\Pictures\beach.jpg", applied.Add, () => true);
            tint.OnPhoto(Photo(dir, Color.FromRgb(210, 20, 25)));
            tint.RunNow();
            Assert.Empty(applied);
        }
        finally { Directory.Delete(dir, true); }
    });

    [Fact]
    public void NoPhoto_NothingHappens() => OnSta(() =>
    {
        var dir = TempDir();
        try
        {
            var applied = new List<string>();
            using var tint = new PhotoTint(dir, () => @"C:\x\nothing_lucent_dark.png", applied.Add, () => false);
            tint.RunNow();
            tint.OnPhoto(Path.Combine(dir, "missing.jpg"));
            tint.RunNow();
            Assert.Empty(applied);
        }
        finally { Directory.Delete(dir, true); }
    });
}
