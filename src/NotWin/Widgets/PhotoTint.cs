using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NotWin.Core.Widgets;

namespace NotWin.Widgets;

/// <summary>
/// The Lucent wallpaper takes its colours from the photo frame (Plan 13 follow-up, user 2026-11-26): each time a frame
/// shows a photo (the slideshow steps about once a minute) or the theme flips, a soft blurred wallpaper is drawn from
/// the photo's three strongest colours — pastel on light grey in light mode, deep on charcoal in dark mode — and set.
/// Only while one of NotWin's own Lucent wallpapers is showing (<see cref="LucentWallpaper.Owns"/>): a wallpaper the
/// user picked is never replaced. Paths are never logged.
/// </summary>
public sealed class PhotoTint : IDisposable
{
    private const int Width = 1600, Height = 900; // soft content: Windows scales it up without visible loss
    private readonly string _dir;
    private readonly Func<string?> _current;
    private readonly Action<string> _apply;
    private readonly Func<bool> _light;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly Random _grain = new(7);
    private string? _photo;
    private bool _disposed;

    /// <param name="dir">Where the tinted wallpapers are written (two files, used in turn).</param>
    /// <param name="current">The wallpaper now on the desktop.</param>
    /// <param name="apply">Sets a wallpaper file.</param>
    /// <param name="light">True in light mode.</param>
    public PhotoTint(string dir, Func<string?> current, Action<string> apply, Func<bool> light)
    {
        _dir = dir;
        _current = current;
        _apply = apply;
        _light = light;
        _debounce.Tick += (_, _) => { _debounce.Stop(); RunNow(); };
    }

    /// <summary>A photo frame now shows this photo (debounced).</summary>
    public void OnPhoto(string path)
    {
        _photo = path;
        Schedule();
    }

    /// <summary>The theme changed: tint again for the new mode (debounced).</summary>
    public void Schedule()
    {
        if (_disposed) return;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Draws and sets the tinted wallpaper now, when allowed.</summary>
    public void RunNow()
    {
        if (_disposed || _photo is not { } photo || !File.Exists(photo)) return;
        var current = _current();
        if (!LucentWallpaper.Owns(current)) return;
        try
        {
            var light = _light();
            var colours = PhotoPalette.Lucent(Read(photo), light);
            Directory.CreateDirectory(_dir);
            var path = Path.Combine(_dir, LucentWallpaper.FileName(light, current));
            Save(Draw(colours, light), path);
            _apply(path);
            Log.Info($"lucent wallpaper tinted from the frame photo ({(light ? "light" : "dark")})");
        }
        catch (Exception e)
        {
            Log.Error("tinting the wallpaper failed", e);
        }
    }

    // The photo's three strongest colours, from a 48 px copy.
    private static IReadOnlyList<(byte R, byte G, byte B)> Read(string photo)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(photo);
        image.DecodePixelWidth = 48;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var px = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(px, bgra.PixelWidth * 4, 0);
        return PhotoPalette.Extract(px, bgra.PixelWidth, bgra.PixelHeight);
    }

    // Big out-of-focus blobs on the base colour (positions as in wallpapers/make_wallpapers.py, section E), then grain.
    private BitmapSource Draw(LucentColours c, bool light)
    {
        static Color C((byte R, byte G, byte B) x, byte a = 255) => Color.FromArgb(a, x.R, x.G, x.B);
        var visual = new DrawingVisual();
        var min = Math.Min(Width, Height);
        (double X, double Y, double R)[] spots = [(0.20, 0.22, 0.85), (0.80, 0.30, 0.80), (0.55, 0.92, 0.90)];
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(C(c.Base)), null, new Rect(0, 0, Width, Height));
            for (var i = 0; i < spots.Length && i < c.Blobs.Count; i++)
            {
                var blob = c.Blobs[i];
                var brush = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(C(blob), 0), new GradientStop(C(blob, 150), 0.45), new GradientStop(C(blob, 0), 1),
                    },
                };
                var r = spots[i].R * min;
                dc.DrawEllipse(brush, null, new Point(spots[i].X * Width, spots[i].Y * Height), r, r);
            }
            var glow = light ? Color.FromArgb(150, 0xF4, 0xF4, 0xF6) : Color.FromArgb(90, 0x2A, 0x2A, 0x2E);
            var lift = new RadialGradientBrush { GradientStops = { new GradientStop(glow, 0), new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 1) } };
            dc.DrawEllipse(lift, null, new Point(Width * 0.98, Height * 0.98), min * 0.45, min * 0.45);
        }
        var rendered = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        var px = new byte[Width * Height * 4];
        rendered.CopyPixels(px, Width * 4, 0);
        var amp = light ? 3.0 : 2.6; // monochrome film grain
        for (var i = 0; i < px.Length; i += 4)
        {
            var n = (int)Math.Round((_grain.NextDouble() + _grain.NextDouble() - 1) * amp * 1.7);
            px[i] = Clamp(px[i] + n);
            px[i + 1] = Clamp(px[i + 1] + n);
            px[i + 2] = Clamp(px[i + 2] + n);
        }
        var result = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Pbgra32, null, px, Width * 4);
        result.Freeze();
        return result;
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

    private static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var f = File.Create(path);
        encoder.Save(f);
    }

    public void Dispose()
    {
        _disposed = true;
        _debounce.Stop();
    }
}
