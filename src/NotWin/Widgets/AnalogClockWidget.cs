using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using NotWin.Core.Widgets;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// Analogue clock (Plan 13, 1×1 / 2×2 circle), no numerals: 12 ticks, the 12 / 3 / 6 / 9 ticks longer. Light face: light
/// card, ink hour and minute hands, a thin red seconds hand, ticking once a second. Dark face: dark card, chunky light
/// hands with round ends, a red centre dot, no seconds hand, ticking once a minute. Stops while paused.
/// </summary>
public sealed class AnalogClockWidget : IDesktopWidget
{
    private readonly Func<DateTime> _now;
    private readonly DispatcherTimer _second = new();
    private readonly MinuteTicker _minute;
    private Face? _face;
    private bool _light = true, _paused, _disposed;

    public AnalogClockWidget(Func<DateTime> now)
    {
        _now = now;
        _second.Tick += (_, _) => { Redraw(); ScheduleSecond(); };
        _minute = new MinuteTicker(Redraw);
    }

    public WidgetKind Kind => WidgetKind.AnalogClock;
    public Surface Surface => _light ? Surface.Light : Surface.Dark;

    /// <summary>The light face has a seconds hand (and ticks every second).</summary>
    internal bool ShowsSeconds => _light;

    internal bool Ticking => _second.IsEnabled || _minute.Running;

    public void Configure(WidgetConfig? config) => _light = WidgetChoices.Normalize(WidgetKind.AnalogClock, config?.Choice) != "dark";

    public FrameworkElement Build(WidgetSize size)
    {
        _face = new Face(_light) { Time = TimeOnly.FromDateTime(_now()) };
        Restart();
        return _face;
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        Restart();
    }

    private void Restart()
    {
        _second.Stop();
        _minute.Stop();
        if (_paused || _disposed || _face is null) return;
        Redraw();
        if (_light) ScheduleSecond();
        else _minute.Start();
    }

    private void ScheduleSecond()
    {
        _second.Stop();
        _second.Interval = TimeSpan.FromMilliseconds(1000 - DateTime.Now.Millisecond + 20);
        _second.Start();
    }

    private void Redraw()
    {
        if (_face is not null) _face.Time = TimeOnly.FromDateTime(_now());
    }

    public void Dispose()
    {
        _disposed = true;
        _second.Stop();
        _minute.Dispose();
    }

    /// <summary>The face, drawn to fit the smaller side of the space it gets.</summary>
    private sealed class Face(bool light) : FrameworkElement
    {
        private static readonly Brush Ink = TileChrome.Frozen(0x10, 0x10, 0x10);
        private TimeOnly _time;

        public TimeOnly Time
        {
            get => _time;
            set
            {
                if (_time == value) return;
                _time = value;
                InvalidateVisual();
            }
        }

        protected override Size MeasureOverride(Size available) =>
            new(double.IsInfinity(available.Width) ? 100 : available.Width, double.IsInfinity(available.Height) ? 100 : available.Height);

        protected override void OnRender(DrawingContext dc)
        {
            var r = Math.Min(ActualWidth, ActualHeight) / 2;
            if (r <= 0) return;
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            var hand = light ? Ink : WidgetTheme.Text; // the dark face is the theme's card (light grey in light mode)
            var tickBrush = light ? Ink : TileChrome.Frozen(0xA6, 0xA6, 0xA6); // brighter ticks read on the dark wallpaper (user 2026-11-28)
            // The dark face draws its own dial — a touch lighter than the card, with a thin ring — so the clock is a
            // distinct object on the near-black wallpaper rather than faint hands floating on black.
            if (!light)
            {
                var dial = TileChrome.Frozen(0x24, 0x24, 0x24);
                var ring = new Pen(TileChrome.Frozen(0x3E, 0x3E, 0x3E), Math.Max(1, r * 0.018));
                dc.DrawEllipse(dial, ring, c, r * 0.97, r * 0.97);
            }
            for (var i = 0; i < 12; i++)
            {
                var major = i % 3 == 0;
                var pen = new Pen(tickBrush, r * (major ? 0.035 : 0.02)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, At(c, i * 30, r * (major ? 0.74 : 0.82)), At(c, i * 30, r * 0.9));
            }
            var (h, m, s) = ClockHands.Angles(_time);
            var thick = light ? 0.06 : 0.1;
            Hand(dc, c, h, r * 0.5, r * thick, hand);
            Hand(dc, c, m, r * 0.74, r * thick * 0.8, hand);
            if (light)
            {
                var red = new Pen(TileChrome.Red, Math.Max(1, r * 0.018)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(red, At(c, s + 180, r * 0.16), At(c, s, r * 0.82));
                dc.DrawEllipse(TileChrome.Red, null, c, r * 0.045, r * 0.045);
            }
            else
            {
                dc.DrawEllipse(TileChrome.Red, null, c, r * 0.07, r * 0.07);
            }
        }

        private static void Hand(DrawingContext dc, Point c, double angle, double length, double width, Brush brush)
        {
            var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, c, At(c, angle, length));
        }

        private static Point At(Point c, double degrees, double length)
        {
            var a = degrees * Math.PI / 180;
            return new Point(c.X + length * Math.Sin(a), c.Y - length * Math.Cos(a));
        }
    }
}
