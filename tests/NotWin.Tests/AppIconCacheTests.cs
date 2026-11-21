using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotWin.Dock;

namespace NotWin.Tests;

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

    // User report 2026-11-20: WhatsApp showed a generic icon in the dock. A Store app's exe inside WindowsApps has no
    // real icon of its own; the icon comes from its package, through its app id (shell:AppsFolder\family!app).
    [Fact]
    public void ParsingName_StoreAppExe_UsesItsAppId()
    {
        const string manifest = """<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Applications><Application Id="WhatsAppDesktop" Executable="WhatsApp.Root.exe" /></Applications></Package>""";
        var exe = @"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop_2.2546.3.0_x64__cv1g1gvanyjgm\WhatsApp.Root.exe";
        Assert.Equal(@"shell:AppsFolder\5319275A.WhatsAppDesktop_cv1g1gvanyjgm!WhatsAppDesktop", AppIconCache.ParsingName(exe, _ => manifest));
    }

    [Fact]
    public void ParsingName_OrdinaryExeAndAppIds_Unchanged()
    {
        Assert.Equal(@"C:\Program Files\Google\Chrome\Application\chrome.exe", AppIconCache.ParsingName(@"C:\Program Files\Google\Chrome\Application\chrome.exe", _ => null));
        Assert.Equal(@"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude", AppIconCache.ParsingName("Claude_pzs8sxrjxfjjc!Claude", _ => null));
        // A Store exe whose manifest cannot be read keeps the exe (the old behaviour).
        var exe = @"C:\Program Files\WindowsApps\A.B_1.0_x64__pub.exe";
        Assert.Equal(exe, AppIconCache.ParsingName(exe, _ => null));
    }
}
