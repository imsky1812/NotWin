using System.Windows.Controls;
using System.Windows.Media;
using NotWin.Core.Widgets;
using NotWin.Widgets;
using static NotWin.Tests.WidgetSurfaceTests;

namespace NotWin.Tests;

/// <summary>Plan 13 follow-up (user 2026-11-26): the snake widget looks right in light mode and plays better.</summary>
[Collection("WidgetTheme")]
public sealed class SnakeLookTests
{
    private static Grid Overlay(System.Windows.FrameworkElement root) => (Grid)((Grid)root).Children[1];

    [Fact]
    public void LightMode_StartShade_IsLight_NotAGreySlab() => OnSta(() =>
    {
        try
        {
            WidgetTheme.Apply(light: true);
            var snake = new SnakeWidget(new SnakeKeyGate(), 3, _ => { });
            var shade = ((SolidColorBrush)Overlay(snake.Build(new WidgetSize(2, 2))).Background).Color;
            Assert.True(shade.R > 200 && shade.G > 200 && shade.B > 200, $"light shade: {shade}");
            WidgetTheme.Apply(light: false);
            shade = ((SolidColorBrush)Overlay(snake.Build(new WidgetSize(2, 2))).Background).Color;
            Assert.True(shade.R < 40, $"dark shade: {shade}");
            snake.Dispose();
        }
        finally { WidgetTheme.Apply(light: false); }
    });

    [Fact]
    public void Speeds_Up_With_The_Score() => OnSta(() =>
    {
        var snake = new SnakeWidget(new SnakeKeyGate(), 0, _ => { });
        snake.Build(new WidgetSize(2, 2));
        Assert.Equal(SnakeGame.TickFor(0), snake.TickInterval);
        snake.Dispose();
    });

    [Fact]
    public void GameOver_ShowsBest_And_NewBestInRed() => OnSta(() =>
    {
        var snake = new SnakeWidget(new SnakeKeyGate(), 0, _ => { });
        var root = snake.Build(new WidgetSize(2, 2));
        snake.ShowOverForTest(score: 7, best: 7, newBest: true);
        var texts = ClockWidgetsTests.Tree(root).OfType<TextBlock>().ToList();
        Assert.Contains(texts, t => t.Text == "NEW BEST" && ((SolidColorBrush)t.Foreground).Color == Color.FromRgb(0xD7, 0x19, 0x21));
        snake.ShowOverForTest(score: 3, best: 7, newBest: false);
        texts = ClockWidgetsTests.Tree(root).OfType<TextBlock>().ToList();
        Assert.Contains(texts, t => t.Text == "BEST 7");
        snake.Dispose();
    });
}
