using System.Windows.Controls;
using System.Windows.Media;
using NotWin.Core.Widgets;
using NotWin.Widgets;
using static NotWin.Tests.WidgetSurfaceTests;

namespace NotWin.Tests;

/// <summary>Plan 13 follow-up: widget colours follow Windows' light / dark app theme (cards are rebuilt on a change).</summary>
[Collection("WidgetTheme")] // the theme is app-wide: these tests must not run beside each other
public sealed class WidgetThemeTests
{
    private static Color C(Brush b) => ((SolidColorBrush)b).Color;

    [Fact]
    public void Light_RecoloursDarkCardsInPlace() => OnSta(() =>
    {
        try
        {
            WidgetTheme.Apply(light: false);
            var card = (Border)WidgetCard.Wrap(new Grid(), Surface.Dark, false, new WidgetSize(2, 2), bare: false);
            Assert.Equal(Color.FromRgb(0x1A, 0x1A, 0x1A), C(card.Background));
            Assert.Equal(Color.FromRgb(0xE8, 0xE8, 0xE8), C(WidgetCard.Ink(Surface.Dark)));
            var changed = 0;
            WidgetTheme.Changed += Count;
            void Count() => changed++;
            WidgetTheme.Apply(light: true);
            WidgetTheme.Apply(light: true); // no change: no second rebuild
            WidgetTheme.Changed -= Count;
            Assert.Equal(1, changed);
            card = (Border)WidgetCard.Wrap(new Grid(), Surface.Dark, false, new WidgetSize(2, 2), bare: false);
            Assert.Equal(Color.FromRgb(0xE8, 0xE8, 0xE8), C(card.Background));
            Assert.Equal(Color.FromRgb(0x10, 0x10, 0x10), C(WidgetCard.Ink(Surface.Dark)));
            Assert.Equal(Color.FromRgb(0x10, 0x10, 0x10), C(WidgetTheme.Text));
            Assert.True(WidgetTheme.Light);
        }
        finally { WidgetTheme.Apply(light: false); }
    });

    [Fact]
    public void Red_StaysRed() => OnSta(() =>
    {
        try
        {
            WidgetTheme.Apply(light: true);
            var card = (Border)WidgetCard.Wrap(new Grid(), Surface.Red, true, new WidgetSize(1, 1), bare: false);
            Assert.Equal(Color.FromRgb(0xD7, 0x19, 0x21), C(card.Background));
            Assert.Equal(Colors.White, C(WidgetCard.Ink(Surface.Red)));
        }
        finally { WidgetTheme.Apply(light: false); }
    });

    [Fact]
    public void Label_UsesTheThemeDim() => OnSta(() =>
    {
        var label = WidgetTheme.Label("Today");
        Assert.Equal("TODAY", label.Text);
        Assert.Same(WidgetTheme.Dim, label.Foreground);
    });
}
