using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NotWin.Core.Widgets;
using NotWin.Notch;
using NotWin.Widgets;

namespace NotWin.Tests;

/// <summary>Plan 13: light / red / round cards, the dot ring and the edit-mode choice lists.</summary>
[Collection("WidgetTheme")]
public sealed class WidgetSurfaceTests
{
    internal static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException("on STA: " + error);
    }

    private static Border Wrap(Surface s, bool round, int w = 1, int h = 1) =>
        (Border)WidgetCard.Wrap(new Grid(), s, round, new WidgetSize(w, h), bare: false);

    private static Color Bg(Border b) => ((SolidColorBrush)b.Background).Color;

    [Fact]
    public void Wrap_Dark_IsTodaysCard() => OnSta(() =>
    {
        var b = Wrap(Surface.Dark, false, 2, 2);
        Assert.Equal(Color.FromRgb(0x1A, 0x1A, 0x1A), Bg(b));
        Assert.Equal(new Thickness(1.5), b.BorderThickness);
    });

    [Fact]
    public void Wrap_Light_UsesLightBackgroundAndInk() => OnSta(() =>
    {
        Assert.Equal(Color.FromRgb(0xE8, 0xE8, 0xE8), Bg(Wrap(Surface.Light, true)));
        Assert.Equal(Color.FromRgb(0x10, 0x10, 0x10), ((SolidColorBrush)WidgetCard.Ink(Surface.Light)).Color);
        Assert.Equal(Color.FromRgb(0xE8, 0xE8, 0xE8), ((SolidColorBrush)WidgetCard.Ink(Surface.Dark)).Color);
    });

    [Fact]
    public void Wrap_Red_NoEdge() => OnSta(() =>
    {
        var b = Wrap(Surface.Red, true);
        Assert.Equal(Color.FromRgb(0xD7, 0x19, 0x21), Bg(b));
        Assert.Equal(new Thickness(0), b.BorderThickness);
        Assert.Equal(Colors.White, ((SolidColorBrush)WidgetCard.Ink(Surface.Red)).Color);
    });

    [Fact]
    public void Wrap_Round_RadiusIsHalfShorterSide() => OnSta(() =>
    {
        Assert.Equal(68, Wrap(Surface.Dark, true, 1, 1).CornerRadius.TopLeft);
        Assert.Equal(68, Wrap(Surface.Dark, true, 2, 1).CornerRadius.TopLeft);
        Assert.Equal(68, Wrap(Surface.Dark, true, 1, 2).CornerRadius.TopLeft);
        Assert.Equal(142, Wrap(Surface.Dark, true, 2, 2).CornerRadius.TopLeft);
        Assert.Equal(new Thickness(10), Wrap(Surface.Dark, true).Padding);
    });

    [Fact]
    public void Wrap_Square_Radius26() => OnSta(() =>
    {
        var b = Wrap(Surface.Light, false, 2, 1);
        Assert.Equal(26, b.CornerRadius.TopLeft);
        Assert.Equal(new Thickness(16), b.Padding);
    });

    [Fact]
    public void Wrap_Bare_ReturnsContent() => OnSta(() =>
    {
        var content = new Grid();
        Assert.Same(content, WidgetCard.Wrap(content, Surface.Dark, false, new WidgetSize(3, 1), bare: true));
    });

    [Fact]
    public void ChoiceLabels_Toggle() => Assert.Equal(
        ["Wi-Fi", "Bluetooth", "Dark mode", "Keep awake", "Mic access", "Camera access"],
        ChoicePicker.Labels(WidgetKind.Toggle).Select(o => o.Label));

    [Fact]
    public void ChoiceLabels_Faces() => Assert.Equal(
        [("light", "Light face"), ("dark", "Dark face")], ChoicePicker.Labels(WidgetKind.AnalogClock));

    [Fact]
    public void ChoiceLabels_Zones_SkipMissing()
    {
        var zones = ChoicePicker.Labels(WidgetKind.WorldClock);
        Assert.True(zones.Count >= 20);
        Assert.All(zones, z => Assert.Contains(z.Id, WidgetChoices.Zones));
        Assert.All(zones, z => Assert.False(string.IsNullOrWhiteSpace(z.Label)));
    }

    [Fact]
    public void DotRingView_Measures() => OnSta(() =>
    {
        var ring = new DotRingView { Count = 60, Lit = 15, Radius = 50, Dot = 4 };
        ring.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(104, 104), ring.DesiredSize);
    });
}
