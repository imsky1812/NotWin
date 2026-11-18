using System.Windows.Media;
using System.Windows.Media.Imaging;
using NothingShell.Dock;

namespace NothingShell.Tests;

public sealed class AppIconCacheTests
{
    private static ImageSource Pixel()
    {
        var b = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        b.Freeze();
        return b;
    }

    [Fact]
    public void FailedIcon_IsNotRetriedWithin5Minutes()
    {
        var now = 0L;
        var loads = 0;
        var cache = new AppIconCache((_, _) => { loads++; return null; }, () => now);
        cache.Get("broken.exe", 32);
        now += (long)TimeSpan.FromMinutes(4).TotalMilliseconds;
        cache.Get("broken.exe", 32);
        Assert.Equal(1, loads);
    }

    [Fact]
    public void FailedIcon_IsRetriedAfter5Minutes()
    {
        var now = 0L;
        var loads = 0;
        var cache = new AppIconCache((_, _) => { loads++; return null; }, () => now);
        cache.Get("broken.exe", 32);
        now += (long)TimeSpan.FromMinutes(5).TotalMilliseconds + 1;
        cache.Get("broken.exe", 32);
        Assert.Equal(2, loads);
    }

    [Fact]
    public void NegativeCache_IsBounded()
    {
        var cache = new AppIconCache((_, _) => null, () => 0);
        for (var i = 0; i < 1000; i++) cache.Get($"missing{i}.exe", 32);
        Assert.True(cache.NegativeCount <= AppIconCache.MaxNegative);
        Assert.Equal(256, AppIconCache.MaxNegative);
    }

    [Fact]
    public void LoadedIcon_IsCached()
    {
        var loads = 0;
        var px = Pixel();
        var cache = new AppIconCache((_, _) => { loads++; return px; }, () => 0);
        Assert.Same(px, cache.Get("ok.exe", 32));
        Assert.Same(px, cache.Get("ok.exe", 32));
        Assert.Equal(1, loads);
    }
}
