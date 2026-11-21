using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NotWin.Core.Notch;
using NotWin.Timing;

namespace NotWin.Notch.Dashboard.Tiles;

/// <summary>
/// Timer tile (mockup <c>NR.timer</c>): 1×1 grid only (click toggles); 2×1 header, dot mm:ss and play/pause; 2×2
/// header with an mm:ss label, a 10×10 grid, and − / play-pause / +. Updates only while the notch is open.
/// </summary>
public sealed class TimerTile : ITileView
{
    private readonly TimerService _timer;
    private GridTimerView? _grid;
    private DotMatrix? _dots;
    private TextBlock? _label;
    private Border? _play;
    private double _playSize;
    private bool _open;

    public TimerTile(TimerService timer)
    {
        _timer = timer;
        _timer.Changed += OnChanged;
    }

    public TileKind Kind => TileKind.Timer;

    public FrameworkElement Build(TileSize size)
    {
        _grid = null; _dots = null; _label = null; _play = null;
        FrameworkElement tile;
        switch (size)
        {
            case TileSize.One:
                _grid = new GridTimerView(56, 8);
                tile = TileChrome.Tile(_grid, center: true);
                tile.Cursor = Cursors.Hand;
                tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; _timer.Toggle(); };
                break;
            case TileSize.Wide:
            {
                var panel = new DockPanel();
                var header = TileChrome.Header("timer", "Timer");
                DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
                panel.Children.Add(header);
                var row = new DockPanel { VerticalAlignment = VerticalAlignment.Bottom };
                _play = PlayHolder(30);
                DockPanel.SetDock(_play, System.Windows.Controls.Dock.Right);
                row.Children.Add(_play);
                _dots = new DotMatrix { Dot = 3, Gap = 1.1, Fill = TileChrome.Text, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(_dots);
                panel.Children.Add(row);
                tile = TileChrome.Tile(panel);
                break;
            }
            default:
            {
                var panel = new DockPanel();
                _label = TileChrome.Label("");
                var header = TileChrome.Header("timer", "Timer", _label);
                DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
                panel.Children.Add(header);
                var controls = new DockPanel { Margin = new Thickness(6, 0, 6, 0) };
                DockPanel.SetDock(controls, System.Windows.Controls.Dock.Bottom);
                var minus = Step("−", _timer.Minus);
                DockPanel.SetDock(minus, System.Windows.Controls.Dock.Left);
                var plus = Step("+", _timer.Plus);
                DockPanel.SetDock(plus, System.Windows.Controls.Dock.Right);
                _play = PlayHolder(30);
                _play.HorizontalAlignment = HorizontalAlignment.Center;
                controls.Children.Add(minus);
                controls.Children.Add(plus);
                controls.Children.Add(_play);
                panel.Children.Add(controls);
                _grid = new GridTimerView(92, 10) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                panel.Children.Add(_grid);
                tile = TileChrome.Tile(panel);
                break;
            }
        }
        Update();
        return tile;
    }

    private Border PlayHolder(double size)
    {
        _playSize = size;
        var holder = new Border { Width = size, Height = size, Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand };
        holder.MouseLeftButtonUp += (_, e) => { e.Handled = true; _timer.Toggle(); };
        return holder;
    }

    private static TextBlock Step(string glyph, Action act)
    {
        var t = new TextBlock
        {
            Text = glyph, FontFamily = TileChrome.Font, FontSize = 18, Foreground = TileChrome.Frozen(0xAA, 0xAA, 0xAA),
            Width = 24, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand,
        };
        t.MouseLeftButtonUp += (_, e) => { e.Handled = true; act(); };
        return t;
    }

    private void OnChanged()
    {
        if (_open) Update();
    }

    private void Update()
    {
        var running = _timer.Running;
        _grid?.Show(_timer.Fraction, running, _timer.FlashOn);
        if (_dots is not null) _dots.Text = _timer.Text;
        if (_label is not null) _label.Text = _timer.Text;
        if (_play is not null) _play.Child = TileChrome.PlayButton(running, _playSize);
    }

    public void Opened()
    {
        _open = true;
        Update();
    }

    public void Closed() => _open = false;

    public void Dispose() => _timer.Changed -= OnChanged;
}
