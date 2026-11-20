using System.IO;
using System.Windows.Media.Imaging;
using NothingShell.Core.Wallpapers;
using NothingShell.Settings;

namespace NothingShell.Tests;

public sealed class WallpaperMakerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-wall-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (DirectoryNotFoundException) { }
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

    private static (int W, int H) SizeOf(string path)
    {
        using var s = File.OpenRead(path);
        var frame = BitmapDecoder.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    [Fact]
    public void Make_WritesTheWallpaperAtScreenSize_AndAPreview() => OnSta(() =>
    {
        var png = WallpaperMaker.Make(new WallRecipe(WallStyle.HalftoneDark, 7), 1600, 900, _dir);
        Assert.True(File.Exists(png));
        Assert.Equal((1600, 900), SizeOf(png));
        var preview = WallpaperMaker.PreviewOf(png);
        Assert.True(File.Exists(preview));
        Assert.Equal(1280, SizeOf(preview).W);
    });

    [Fact]
    public void List_NewestFirst_Prune_KeepsTwelveAndTheCurrentOne() => OnSta(() =>
    {
        var made = new List<string>();
        for (var i = 0; i < 15; i++)
        {
            made.Add(WallpaperMaker.Make(new WallRecipe(WallStyle.Grid, i), 320, 180, _dir));
            File.SetLastWriteTimeUtc(made[^1], DateTime.UtcNow.AddMinutes(i)); // a clear order
        }
        Assert.Equal(made.AsEnumerable().Reverse(), WallpaperMaker.List(_dir));

        WallpaperMaker.Prune(_dir, keep: made[0]); // the oldest is the current wallpaper
        var left = WallpaperMaker.List(_dir);
        Assert.Equal(13, left.Count);
        Assert.Contains(made[0], left);
        Assert.DoesNotContain(made[1], left);
        Assert.False(File.Exists(WallpaperMaker.PreviewOf(made[1]))); // its preview went with it
    });
}
