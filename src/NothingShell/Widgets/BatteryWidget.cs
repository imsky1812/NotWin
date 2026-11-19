using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NothingShell.Core.Widgets;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Widgets;

/// <summary>
/// Battery widget (spec §6.5, mockup <c>renderBattery</c> / <c>bRing</c>): a ring and the device's icon, no number.
/// One device fills the tile; with more, page dots on the right (click a dot or scroll), pages slide vertically, and a
/// newly connected device is jumped to. Ring and icon turn red under 20 %; ⚡ when charging; the tooltip shows the
/// exact %. A device that reports no level shows its icon only.
/// </summary>
public sealed class BatteryWidget : IDesktopWidget
{
    private readonly IBatterySource _devices;
    private readonly BatteryPages _pages = new();
    private Grid? _host;
    private WidgetSize _size;
    private int _shown = -1;

    public BatteryWidget(IBatterySource devices)
    {
        _devices = devices;
        _devices.Changed += OnChanged;
        _pages.Update(devices.Devices);
    }

    public WidgetKind Kind => WidgetKind.Battery;

    public FrameworkElement Build(WidgetSize size)
    {
        _size = size;
        _shown = -1;
        _host = new Grid { Margin = new Thickness(-16), Background = Brushes.Transparent, ClipToBounds = true }; // mockup: padding 0
        _host.MouseWheel += (_, e) => { e.Handled = true; _pages.Scroll(e.Delta < 0 ? 1 : -1); Fill(); };
        Fill();
        return _host;
    }

    private void OnChanged()
    {
        _pages.Update(_devices.Devices);
        Fill();
    }

    private void Fill()
    {
        if (_host is null) return;
        var list = _pages.Devices;
        var direction = _shown < 0 ? 0 : Math.Sign(_pages.Current - _shown);
        _shown = _pages.Current;
        _host.Children.Clear();
        if (list.Count == 0)
        {
            var none = TileChrome.Label("No battery");
            none.HorizontalAlignment = HorizontalAlignment.Center;
            none.VerticalAlignment = VerticalAlignment.Center;
            _host.Children.Add(none);
            return;
        }
        var page = Page(list[_pages.Current]);
        _host.Children.Add(page);
        if (direction != 0)
        {
            var shift = new TranslateTransform(0, direction * 60);
            page.RenderTransform = shift;
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimationUsingKeyFrames
            {
                KeyFrames = { new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)), new KeySpline(0.2, 0.8, 0.2, 1)) },
            });
        }
        if (list.Count > 1) _host.Children.Add(Dots(list.Count));
    }

    private FrameworkElement Page(BatteryDevice d)
    {
        var big = _size == new WidgetSize(2, 2);
        var ringSize = big ? 220.0 : 100.0;
        var iconSize = big ? 60.0 : 32.0;
        var low = d.Percent is < 20;
        var ring = new Grid { Width = ringSize, Height = ringSize, ToolTip = d.Percent is { } p ? $"{d.Name} · {p}%" : d.Name };
        if (d.Percent is { } pct) ring.Children.Add(Ring(ringSize, pct, low));
        var icon = TileChrome.Icon(d.Icon, iconSize, low ? TileChrome.Red : TileChrome.Text);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        ring.Children.Add(icon);
        if (d.Charging)
        {
            var bolt = new TextBlock
            {
                Text = "⚡", FontSize = iconSize * 0.45, Foreground = TileChrome.Text,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, ringSize * 0.14),
            };
            ring.Children.Add(bolt);
        }
        if (_size != new WidgetSize(2, 1))
            return new Grid { Children = { ring }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        text.Children.Add(TileChrome.Body(d.Name, 15, FontWeights.SemiBold));
        var state = TileChrome.Label(low ? "Low · charge soon" : d.Percent is null ? "Connected" : "OK", low ? TileChrome.Red : null);
        state.Margin = new Thickness(0, 6, 0, 0);
        text.Children.Add(state);
        return new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { ring, text } };
    }

    // mockup bRing: stroke = size × 0.09, track #2C2C2C, level #F2F2F2 (red under 20 %), round cap, from 12 o'clock.
    private static FrameworkElement Ring(double size, int percent, bool low)
    {
        var stroke = size * 0.09;
        var r = (size - stroke) / 2 - 1;
        var c = size / 2;
        var track = new Ellipse { Width = r * 2, Height = r * 2, Stroke = TileChrome.Frozen(0x2C, 0x2C, 0x2C), StrokeThickness = stroke, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { Width = size, Height = size, Children = { track } };
        var pct = Math.Clamp(percent, 0, 100);
        if (pct <= 0) return grid;
        var angle = Math.Min(pct, 99.9) / 100 * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, pct > 50, SweepDirection.Clockwise, true));
        grid.Children.Add(new Path
        {
            Data = new PathGeometry([figure]), Stroke = low ? TileChrome.Red : TileChrome.Text, StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        return grid;
    }

    private FrameworkElement Dots(int count)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var dot = new Ellipse { Width = 7, Height = 7, Fill = i == _pages.Current ? TileChrome.Text : TileChrome.Frozen(0x44, 0x44, 0x44) };
            var hit = new Border { Padding = new Thickness(3, 3.5, 3, 3.5), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = dot };
            hit.MouseLeftButtonUp += (_, e) => { e.Handled = true; _pages.Go(index); Fill(); };
            stack.Children.Add(hit);
        }
        return stack;
    }

    public void Dispose() => _devices.Changed -= OnChanged;
}
