using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NotWin.Core;
using NotWin.Core.Widgets;
using NotWin.Native;
using NotWin.Notch;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// Clock widget (spec §6.5, mockup <c>renderClock</c>): no card — a date line and big dot-matrix HH:MM drawn on the
/// wallpaper; dark dots on light wallpapers, light dots on dark ones (the wallpaper under the clock is sampled when the
/// clock moves or the wallpaper changes). Dots 12/4 at 3×1, 7/3 at 2×1, 17/5 at 4×2. Ticks once a minute.
/// </summary>
public sealed class ClockWidget : IDesktopWidget
{
    private static readonly Brush Dark = TileChrome.Frozen(0x14, 0x14, 0x14), Light = TileChrome.Text;
    private static readonly Brush DateOnDark = TileChrome.Frozen(0xBB, 0xBB, 0xBB), DateOnLight = TileChrome.Frozen(0x33, 0x33, 0x33);
    private readonly MinuteTicker _minute;
    private DotMatrix? _dots;
    private TextBlock? _date;
    private bool _darkDots;
    private RectI _screen, _monitor;

    public ClockWidget()
    {
        _minute = new MinuteTicker(Tick);
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    public WidgetKind Kind => WidgetKind.Clock;
    public bool Bare => true;

    public FrameworkElement Build(WidgetSize size)
    {
        var (dot, gap, font) = size switch
        {
            { W: 4 } => (17.0, 5.0, 20.0),
            { W: 2 } => (7.0, 3.0, 14.0),
            _ => (12.0, 4.0, 14.0),
        };
        _date = new TextBlock { FontFamily = TileChrome.Font, FontSize = font, Margin = new Thickness(0, 0, 0, size.W == 2 ? 6 : 10) };
        _dots = new DotMatrix { Dot = dot, Gap = gap };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _date, _dots } };
        Tick();
        Paint();
        return stack;
    }

    public void Placed(RectI screen, RectI monitor)
    {
        _screen = screen;
        _monitor = monitor;
        Sample();
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Desktop) Application.Current?.Dispatcher.BeginInvoke(Sample);
    }

    private void Sample()
    {
        var lum = WallpaperSampler.MeanLuminance(WallpaperNative.Current(), _screen, _monitor);
        _darkDots = lum is { } l && ClockInk.For(l);
        Paint();
    }

    private void Paint()
    {
        if (_dots is not null) _dots.Fill = _darkDots ? Dark : Light;
        if (_date is not null) _date.Foreground = _darkDots ? DateOnLight : DateOnDark;
    }

    private void Tick()
    {
        var now = DateTime.Now;
        if (_dots is not null) _dots.Text = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (_date is not null) _date.Text = now.ToString("ddd, d MMM", CultureInfo.InvariantCulture);
        if (!_minute.Running) _minute.Start();
    }

    public void Dispose()
    {
        _minute.Stop();
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
    }
}
