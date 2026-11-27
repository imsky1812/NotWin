using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NotWin.Clips;
using NotWin.Core.Forecast;
using NotWin.Media;
using NotWin.Timing;
using NotWin.Notch;
using NotWin.Notch.Dashboard.Tiles;
using static NotWin.Tests.WidgetSurfaceTests;

namespace NotWin.Tests;

/// <summary>User 2026-11-27: "the weather tile temp degree is overlapping" — the temperature and the city never touch.</summary>
public sealed class NotchTileLayoutTests
{
    private static readonly WeatherReport Report = new(-12, 25, 12, WeatherKind.Clear, true,
        [.. Enumerable.Range(0, 5).Select(i => new WeatherDay(new DateOnly(2026, 11, 27).AddDays(i), 25, 12, WeatherKind.Clear))],
        DateTimeOffset.Now);

    private static Rect Bounds(FrameworkElement e, Visual root) =>
        e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));

    [Theory]
    [InlineData(152, 58, false)]   // a 2×1 tile's content area
    [InlineData(152, 152, true)]   // a 2×2 tile's content area
    public void Weather_TemperatureAndCity_DoNotOverlap(double w, double h, bool big) => OnSta(() =>
    {
        var place = new WeatherPlace("Ghaziabad Extended Name", "IN", 28.6, 77.4);
        var view = big ? WeatherTile.Big(place, Report, false) : WeatherTile.Wide(place, Report, false);
        var host = new Border { Width = w, Height = h, Child = view };
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        var temp = ClockWidgetsTests.Tree(host).OfType<DotMatrix>().First();
        var city = ClockWidgetsTests.Tree(host).OfType<TextBlock>().First(t => t.Text.StartsWith("GHAZIABAD", StringComparison.Ordinal));
        var a = Bounds(temp, host);
        var b = Bounds(city, host);
        Assert.False(a.IntersectsWith(b) && a.Right > b.Left + 0.5, $"temp {a} overlaps city {b}");
        Assert.True(a.Right <= w + 0.5 && b.Right <= w + 0.5, "both fit the tile");
    });

    // A usual city name and the high / low read in full beside "-12°", nothing cut to "GHA…" or "↑25…".
    [Theory]
    [InlineData(152, 58, false)]
    [InlineData(152, 152, true)]
    public void Weather_CityAndRange_ShowInFull(double w, double h, bool big) => OnSta(() =>
    {
        var place = new WeatherPlace("Ghaziabad", "IN", 28.6, 77.4);
        var view = big ? WeatherTile.Big(place, Report, false) : WeatherTile.Wide(place, Report, false);
        var host = new Border { Width = w, Height = h, Child = view };
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        foreach (var t in ClockWidgetsTests.Tree(host).OfType<TextBlock>().Where(t => t.Text.StartsWith("GHAZIABAD", StringComparison.Ordinal) || t.Text.Contains('↑') || t.Text.Contains('↓')))
        {
            var full = new TextBlock { Text = t.Text, FontFamily = t.FontFamily, FontSize = t.FontSize, FontWeight = t.FontWeight };
            full.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(t.ActualWidth + 0.5 >= full.DesiredSize.Width, $"'{t.Text}' cut: {t.ActualWidth} < {full.DesiredSize.Width}");
        }
    });

    private static Border Host(FrameworkElement view, double w, double h)
    {
        var host = new Border { Width = w, Height = h, Child = view };
        host.Measure(new Size(w, h));
        host.Arrange(new Rect(0, 0, w, h));
        host.UpdateLayout();
        return host;
    }

    private static void AssertNotCut(TextBlock t)
    {
        var full = new TextBlock { Text = t.Text, FontFamily = t.FontFamily, FontSize = t.FontSize, FontWeight = t.FontWeight };
        full.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.True(t.ActualWidth + 0.5 >= full.DesiredSize.Width, $"'{t.Text}' cut: {t.ActualWidth} < {full.DesiredSize.Width}");
    }

    // User 2026-11-27 (screenshot): "IMAGE · 970 ×" — an image row's size was cut off in the 2×2 clipboard tile.
    [Fact]
    public void Clipboard_ImageRow_ShowsItsSizeInFull() => OnSta(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "nw-clip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var clips = new ClipboardService(dir, watch: false);
            clips.History.AddImage("abc", 1920, 1080);
            var tile = new ClipboardTile(clips, () => null!);
            var host = Host(tile.Build(Core.Notch.TileSize.Big), 178, 178);
            var size = ClockWidgetsTests.Tree(host).OfType<TextBlock>().Single(t => t.Text.Contains("1920", StringComparison.Ordinal));
            AssertNotCut(size);
            Assert.Contains("1080", size.Text, StringComparison.Ordinal);
            var r = Bounds(size, host);
            Assert.True(r.Right <= 178 - 12 - 8 + 0.5, $"size text ends at {r.Right}, past the row's edge"); // tile and row padding
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    });

    // The music seek dots run the width of the tile, not two thirds of it.
    [Fact]
    public void Media_SeekDots_SpanTheTile() => OnSta(() =>
    {
        using var media = new MediaSessionService(() => Task.FromResult<NotWin.Media.ISessionManager?>(null));
        var tile = new MediaTile(media, _ => "App");
        var host = Host(tile.Build(Core.Notch.TileSize.Big), 178, 178);
        var bar = ClockWidgetsTests.Tree(host).OfType<DotBar>().Single();
        var inner = 178 - 2 * 12; // the tile's padding
        Assert.True(bar.ActualWidth >= inner - 8, $"seek dots {bar.ActualWidth} wide in a {inner} tile");
    });

    // The timer's − and + are round chips the size of its play button (they were bare glyphs).
    [Fact]
    public void Timer_Steppers_AreRoundChips() => OnSta(() =>
    {
        var tile = new TimerTile(new TimerService(1));
        var host = Host(tile.Build(Core.Notch.TileSize.Big), 178, 178);
        foreach (var glyph in new[] { "−", "+" })
        {
            var t = ClockWidgetsTests.Tree(host).OfType<TextBlock>().Single(x => x.Text == glyph);
            var chip = Assert.IsType<Border>(t.Parent);
            Assert.True(chip.ActualWidth >= 28 && Math.Abs(chip.ActualWidth - chip.ActualHeight) < 0.5, $"{glyph} chip {chip.ActualWidth}×{chip.ActualHeight}");
            Assert.True(chip.CornerRadius.TopLeft >= chip.ActualWidth / 2 - 0.5);
        }
    });
}
