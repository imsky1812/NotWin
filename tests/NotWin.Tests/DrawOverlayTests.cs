using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotWin.Tools;

namespace NotWin.Tests;

public sealed class DrawOverlayTests
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

    private static Color PixelAt(BitmapSource b, int x, int y)
    {
        var px = new byte[4];
        new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return Color.FromRgb(px[2], px[1], px[0]);
    }

    [Fact]
    public void Compose_DrawsTheStrokesOnTheShot_AtFullPixelResolution() => OnSta(() =>
    {
        // A 200x100 px white "screen" at 200 % (100x50 DIP); a red stroke along y = 10 DIP from x = 10 to 60 DIP.
        var white = new byte[200 * 100 * 4];
        Array.Fill(white, (byte)255);
        var shot = BitmapSource.Create(200, 100, 96, 96, PixelFormats.Bgra32, null, white, 200 * 4);
        var stroke = new Stroke(new StylusPointCollection([new StylusPoint(10, 10), new StylusPoint(60, 10)]))
        {
            DrawingAttributes = { Color = Colors.Red, Width = 4, Height = 4 },
        };

        var result = DrawOverlay.Compose(shot, new StrokeCollection([stroke]), scale: 2);

        Assert.Equal(200, result.PixelWidth);
        Assert.Equal(100, result.PixelHeight);
        var onStroke = PixelAt(result, 70, 20);   // DIP (35, 10) at 200 %
        Assert.True(onStroke.R > 200 && onStroke.G < 80 && onStroke.B < 80, $"expected red, got {onStroke}");
        Assert.Equal(Colors.White, PixelAt(result, 150, 80)); // untouched screen stays as it was
    });
}
