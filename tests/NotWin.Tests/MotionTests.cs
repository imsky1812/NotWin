using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NotWin.Theme;
using static NotWin.Tests.WidgetSurfaceTests;

namespace NotWin.Tests;

/// <summary>Plan 13 follow-up (user 2026-11-27): every popup and transition quick and smooth.</summary>
[Collection("Motion")]
public sealed class MotionTests
{
    [Fact]
    public void Durations_AreQuick()
    {
        Assert.InRange(Motion.In.TotalMilliseconds, 120, 200);
        Assert.InRange(Motion.Out.TotalMilliseconds, 80, 130);
        Assert.True(Motion.Out < Motion.In, "leaving is quicker than arriving");
    }

    [Fact]
    public void PopIn_Animates_FromSlightlySmallerAndClear() => OnSta(() =>
    {
        var el = new Border();
        Motion.Enabled = true;
        try
        {
            Motion.PopIn(el);
            Assert.True(el.HasAnimatedProperties);
            var group = Assert.IsType<TransformGroup>(el.RenderTransform);
            Assert.Contains(group.Children, t => t is ScaleTransform);
            Assert.Equal(new Point(0.5, 0.5), el.RenderTransformOrigin);
        }
        finally { Motion.Enabled = null; }
    });

    [Fact]
    public void PopIn_KeepsAnExistingTransform() => OnSta(() =>
    {
        var shift = new TranslateTransform(0, 8);
        var el = new Border { RenderTransform = shift };
        Motion.Enabled = true;
        try
        {
            Motion.PopIn(el);
            Assert.Same(shift, el.RenderTransform); // only the opacity animates
        }
        finally { Motion.Enabled = null; }
    });

    [Fact]
    public void AnimationsOff_EverythingIsInstant() => OnSta(() =>
    {
        Motion.Enabled = false;
        try
        {
            var el = new Border { Opacity = 0 };
            Motion.PopIn(el);
            Assert.Equal(1, el.Opacity);
            Assert.False(el.HasAnimatedProperties);
            var done = false;
            Motion.PopOut(el, () => done = true);
            Assert.True(done);
        }
        finally { Motion.Enabled = null; }
    });

    [Fact]
    public void PopOut_Enabled_FinishesLater() => OnSta(() =>
    {
        Motion.Enabled = true;
        try
        {
            var el = new Border();
            var done = false;
            Motion.PopOut(el, () => done = true);
            Assert.False(done); // after the short exit, not before
            Assert.True(el.HasAnimatedProperties);
        }
        finally { Motion.Enabled = null; }
    });

    // One hook per popup window: it arrives with PopIn and every Close() leaves with PopOut first.
    [Fact]
    public void Smooth_Window_FadesOutBeforeClosing() => OnSta(() =>
    {
        Motion.Enabled = true;
        try
        {
            var w = new Window { Content = new Border(), Width = 50, Height = 50, ShowActivated = false, Left = -2000, ShowInTaskbar = false };
            Motion.Smooth(w);
            w.Show();
            Assert.True(((Border)w.Content).HasAnimatedProperties); // popped in
            w.Close();
            Assert.True(w.IsVisible); // still fading out, not gone yet
            Motion.Enabled = false;
            w.Close(); // a second Close while fading closes at once
            Assert.False(w.IsVisible);
        }
        finally { Motion.Enabled = null; }
    });

    [Fact]
    public void Smooth_Window_AnimationsOff_ClosesAtOnce() => OnSta(() =>
    {
        Motion.Enabled = false;
        try
        {
            var w = new Window { Content = new Border(), Width = 50, Height = 50, ShowActivated = false, Left = -2000, ShowInTaskbar = false };
            Motion.Smooth(w);
            w.Show();
            w.Close();
            Assert.False(w.IsVisible);
        }
        finally { Motion.Enabled = null; }
    });

    [Fact]
    public void Stagger_DelaysEachTileALittle_Capped()
    {
        Assert.Equal(TimeSpan.Zero, Motion.Stagger(0));
        Assert.Equal(TimeSpan.FromMilliseconds(18), Motion.Stagger(1));
        Assert.Equal(Motion.Stagger(8), Motion.Stagger(20)); // never waits long for the last tiles
    }
}
