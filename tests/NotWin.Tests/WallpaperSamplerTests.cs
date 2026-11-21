using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotWin.Core;
using NotWin.Widgets;

namespace NotWin.Tests;

// Review minor (Plan 5): Windows rewrites its wallpaper file in place (same path), so the sampler must notice a new
// picture behind an unchanged path.
public sealed class WallpaperSamplerTests
{
    private static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException("on STA: " + error);
    }

    private static void SavePng(string path, byte gray, int size)
    {
        var pixels = Enumerable.Repeat(gray, size * size).ToArray();
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Gray8, null, pixels, size);
        using var fs = File.Create(path);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        enc.Save(fs);
    }

    [Fact]
    public void SamePath_NewPicture_IsReadAgain() => OnSta(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "ns-wall-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var mon = new RectI(0, 0, 100, 100);
            SavePng(path, 255, 64);
            Assert.True(WallpaperSampler.MeanLuminance(path, mon, mon) > 0.9);
            SavePng(path, 0, 80); // a new picture, same file
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
            Assert.True(WallpaperSampler.MeanLuminance(path, mon, mon) < 0.1);
        }
        finally { File.Delete(path); }
    });
}
