using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NotWin.Theme;

/// <summary>
/// NotWin's shared motion (Plan 13 follow-up, user 2026-11-27: "every popup and transition smoother, but quick, it should
/// not wait too long"). Popups arrive with a short scale-and-fade (170 ms, ease-out) and leave quicker (110 ms); notch
/// tiles fade up in a light stagger; tiles lift on hover and dip on press. Everything is instant when Windows'
/// animation effects are off (Settings › Accessibility › Visual effects).
/// </summary>
public static class Motion
{
    public static readonly TimeSpan In = TimeSpan.FromMilliseconds(170);
    public static readonly TimeSpan Out = TimeSpan.FromMilliseconds(110);
    public static readonly TimeSpan Quick = TimeSpan.FromMilliseconds(140);
    private static readonly KeySpline EaseOut = Frozen(new KeySpline(0.2, 0.8, 0.2, 1));
    private static readonly KeySpline EaseIn = Frozen(new KeySpline(0.4, 0, 1, 1));
    private const int StaggerMs = 18, StaggerCap = 8;

    /// <summary>Overrides the system setting (tests); null = follow Windows' animation effects.</summary>
    public static bool? Enabled { get; set; }

    private static bool On => Enabled ?? SystemParameters.ClientAreaAnimation;

    /// <summary>Arrives: fades in from clear while growing from <paramref name="fromScale"/> and rising <paramref name="fromY"/>.</summary>
    public static void PopIn(UIElement el, double fromScale = 0.96, double fromY = 6, TimeSpan? delay = null)
    {
        if (!On)
        {
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 1;
            return;
        }
        var begin = delay ?? TimeSpan.Zero;
        el.BeginAnimation(UIElement.OpacityProperty, Anim(0, 1, In, EaseOut, begin));
        // An element that already moves itself (the panel's slide) keeps its own transform: only the fade.
        if (el.RenderTransform is not (null or MatrixTransform { Matrix.IsIdentity: true } or TransformGroup { Children.Count: 2 })) return;
        var (scale, shift) = Transforms(el);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(fromScale, 1, In, EaseOut, begin));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(fromScale, 1, In, EaseOut, begin));
        shift.BeginAnimation(TranslateTransform.YProperty, Anim(fromY, 0, In, EaseOut, begin));
    }

    /// <summary>Leaves: a quick fade (and a hint of shrink), then <paramref name="done"/> (at once when animations are off).</summary>
    public static void PopOut(UIElement el, Action done)
    {
        if (!On)
        {
            done();
            return;
        }
        var fade = Anim(el.Opacity, 0, Out, EaseIn, TimeSpan.Zero);
        var finished = false;
        fade.Completed += (_, _) =>
        {
            if (finished) return;
            finished = true;
            done();
        };
        el.BeginAnimation(UIElement.OpacityProperty, fade);
        if (el.RenderTransform is TransformGroup { Children.Count: 2 } && el.RenderTransform.IsFrozen == false)
        {
            var (scale, _) = Transforms(el);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(scale.ScaleX, 0.98, Out, EaseIn, TimeSpan.Zero));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(scale.ScaleY, 0.98, Out, EaseIn, TimeSpan.Zero));
        }
    }

    /// <summary>
    /// A popup window's motion in one call: its content pops in when the window loads, and the first Close() fades it out
    /// quickly before the window really closes (a second Close() while fading, or app shutdown, closes at once).
    /// </summary>
    public static void Smooth(Window window)
    {
        var state = States.GetOrCreateValue(window);
        window.Loaded += (_, _) => { if (window.Content is UIElement c) PopIn(c); };
        window.Closing += (_, e) =>
        {
            if (state.Leaving || !On || window.Content is not UIElement content || Application.Current?.Dispatcher.HasShutdownStarted == true) return;
            state.Leaving = true;
            e.Cancel = true;
            PopOut(content, () =>
            {
                try { window.Close(); }
                catch (InvalidOperationException) { } // already closing
            });
        };
    }

    /// <summary>
    /// Closes a <see cref="Smooth"/> window at once, without the fade: for a popup that must be gone before something
    /// else opens (the photo frame's ＋ opens a file picker that the fading popup would pull down with it).
    /// </summary>
    public static void CloseNow(Window window)
    {
        States.GetOrCreateValue(window).Leaving = true;
        try { window.Close(); }
        catch (InvalidOperationException) { } // already closing
    }

    private sealed class SmoothState
    {
        public bool Leaving;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, SmoothState> States = [];

    /// <summary>The delay before the <paramref name="index"/>th tile of a group arrives: 18 ms apart, never more than 8 steps.</summary>
    public static TimeSpan Stagger(int index) => TimeSpan.FromMilliseconds(StaggerMs * Math.Min(Math.Max(0, index), StaggerCap));

    /// <summary>A tile lifts a little under the pointer (1.02) and dips when pressed (0.98).</summary>
    public static void Hover(FrameworkElement el)
    {
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        void To(double s)
        {
            if (!On) return;
            var (scale, _) = Transforms(el);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(scale.ScaleX, s, Quick, EaseOut, TimeSpan.Zero));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(scale.ScaleY, s, Quick, EaseOut, TimeSpan.Zero));
        }
        el.MouseEnter += (_, _) => To(1.02);
        el.MouseLeave += (_, _) => To(1);
        el.PreviewMouseLeftButtonDown += (_, _) => To(0.98);
        el.PreviewMouseLeftButtonUp += (_, _) => To(el.IsMouseOver ? 1.02 : 1);
    }

    // A scale and a translate the element owns (made once, reused by later animations).
    private static (ScaleTransform Scale, TranslateTransform Shift) Transforms(UIElement el)
    {
        if (el.RenderTransform is TransformGroup { Children.Count: 2 } g && g.Children[0] is ScaleTransform s && g.Children[1] is TranslateTransform t)
            return (s, t);
        var scale = new ScaleTransform(1, 1);
        var shift = new TranslateTransform();
        el.RenderTransform = new TransformGroup { Children = { scale, shift } };
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        return (scale, shift);
    }

    private static DoubleAnimationUsingKeyFrames Anim(double from, double to, TimeSpan length, KeySpline ease, TimeSpan begin) => new()
    {
        BeginTime = begin,
        FillBehavior = FillBehavior.HoldEnd,
        KeyFrames =
        {
            new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)),
            new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(length), ease),
        },
    };

    private static KeySpline Frozen(KeySpline k)
    {
        k.Freeze();
        return k;
    }
}
