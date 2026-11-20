using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NothingShell.Widgets;

namespace NothingShell.Tests;

public sealed class PhotoStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ns-frames-" + Guid.NewGuid().ToString("N"));
    private readonly string _src = Path.Combine(Path.GetTempPath(), "ns-src-" + Guid.NewGuid().ToString("N"));

    public PhotoStoreTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_src);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        try { Directory.Delete(_src, true); } catch { }
    }

    private static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException("on STA: " + error);
    }

    // A solid PNG of the given size, written with WPF.
    private string Png(string name, int w, int h)
    {
        var path = Path.Combine(_src, name);
        OnSta(() =>
        {
            var pixels = new byte[w * h * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 40; pixels[i + 1] = 90; pixels[i + 2] = 200; pixels[i + 3] = 255; }
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            enc.Save(fs);
        });
        return path;
    }

    private static (int W, int H) SizeOf(string path)
    {
        (int, int) size = default;
        OnSta(() =>
        {
            using var fs = File.OpenRead(path);
            var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            size = (frame.PixelWidth, frame.PixelHeight);
        });
        return size;
    }

    [Fact]
    public async Task Import_ScalesLongSideTo1600()
    {
        var store = new PhotoStore(_root);
        var (added, skipped) = await store.ImportAsync("a1b2c3d4", [Png("wide.png", 4000, 1000)]);
        Assert.Equal(0, skipped);
        var name = Assert.Single(added);
        Assert.Equal((1600, 400), SizeOf(store.PhotoPath("a1b2c3d4", name)));
        var thumb = SizeOf(store.ThumbPath("a1b2c3d4", name));
        Assert.Equal(240, Math.Max(thumb.W, thumb.H));
    }

    [Fact]
    public async Task Import_KeepsSmallPhotosAtTheirSize()
    {
        var store = new PhotoStore(_root);
        var (added, _) = await store.ImportAsync("a1b2c3d4", [Png("small.png", 300, 200)]);
        Assert.Equal((300, 200), SizeOf(store.PhotoPath("a1b2c3d4", added[0])));
    }

    [Fact]
    public async Task Import_SkipsUnreadable_CountsOnly()
    {
        var bad = Path.Combine(_src, "x.jpg");
        await File.WriteAllTextAsync(bad, "not an image");
        var store = new PhotoStore(_root);
        var (added, skipped) = await store.ImportAsync("a1b2c3d4", [bad, Png("good.png", 50, 50), Path.Combine(_src, "missing.jpg")]);
        Assert.Single(added);
        Assert.Equal(2, skipped);
    }

    [Fact]
    public async Task Import_DoesNotKeepOriginalNames()
    {
        var store = new PhotoStore(_root);
        var (added, _) = await store.ImportAsync("a1b2c3d4", [Png("holiday-with-mum.png", 40, 40)]);
        Assert.DoesNotContain("holiday", added[0]);
        Assert.EndsWith(".jpg", added[0]);
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(_root, "a1b2c3d4")), f => f.Contains("holiday"));
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatFrame()
    {
        var store = new PhotoStore(_root);
        await store.ImportAsync("aaaaaaaa", [Png("1.png", 20, 20)]);
        await store.ImportAsync("bbbbbbbb", [Png("2.png", 20, 20)]);
        store.Delete("aaaaaaaa");
        Assert.False(Directory.Exists(Path.Combine(_root, "aaaaaaaa")));
        Assert.True(Directory.Exists(Path.Combine(_root, "bbbbbbbb")));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public async Task Delete_RefusesPathLikeIds()
    {
        var store = new PhotoStore(_root);
        await store.ImportAsync("aaaaaaaa", [Png("1.png", 20, 20)]);
        foreach (var id in new[] { "..", "", ".", "a\\b", "a/b", "..\\x" }) store.Delete(id);
        Assert.True(Directory.Exists(Path.Combine(_root, "aaaaaaaa")));
        Assert.True(Directory.Exists(_root));
        Assert.True(Directory.Exists(_src));
    }

    [Fact]
    public async Task Remove_DeletesThePhotoAndItsThumb()
    {
        var store = new PhotoStore(_root);
        var (added, _) = await store.ImportAsync("aaaaaaaa", [Png("1.png", 20, 20), Png("2.png", 20, 20)]);
        store.Remove("aaaaaaaa", added[0]);
        Assert.False(File.Exists(store.PhotoPath("aaaaaaaa", added[0])));
        Assert.False(File.Exists(store.ThumbPath("aaaaaaaa", added[0])));
        Assert.True(File.Exists(store.PhotoPath("aaaaaaaa", added[1])));
        store.Remove("aaaaaaaa", "..\\..\\x.jpg"); // path-like names are ignored
    }
}
