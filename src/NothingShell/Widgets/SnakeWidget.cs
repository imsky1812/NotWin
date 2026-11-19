using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NothingShell.Core.Widgets;
using NothingShell.Native;
using NothingShell.Notch;
using NothingShell.Notch.Dashboard;

namespace NothingShell.Widgets;

/// <summary>
/// Snake widget (spec §6.5, mockup <c>drawSnake</c>): playable on a 14×14 (2×2) or 20×20 (3×3) board. Click to take the
/// keys; arrows / WASD steer, Space pauses or restarts. Keys arrive through the shell's keyboard hook only while armed
/// (<see cref="SnakeKeyGate"/>); switching to any window, a click outside the board, or Esc disarms and pauses. White snake, red food, the score in
/// dots, the best score saved. The 120 ms tick runs only while playing.
/// </summary>
public sealed class SnakeWidget : IDesktopWidget
{
    private static readonly Brush Unlit = TileChrome.Frozen(0x26, 0x26, 0x26), Dead = TileChrome.Frozen(0x77, 0x77, 0x77);
    private readonly SnakeGame _game;
    private readonly Action<int> _saveBest;
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(120) };
    private ForegroundChangeHook? _focus;
    private Board? _board;
    private Grid? _overlay;
    private Border? _ring;
    private Grid? _root;
    private Action<int, int>? _clickWatch;
    private int _savedBest;

    public SnakeWidget(SnakeKeyGate gate, int best, Action<int> saveBest)
    {
        Gate = gate;
        _saveBest = saveBest;
        _savedBest = best;
        _game = new SnakeGame(14, new Random(), best);
        _tick.Tick += (_, _) => Step();
    }

    public SnakeKeyGate Gate { get; }
    public WidgetKind Kind => WidgetKind.Snake;

    public FrameworkElement Build(WidgetSize size)
    {
        var n = size.W >= 3 ? 20 : 14;
        if (_game.Size != n) _game.Resize(n);
        _board = new Board(_game);
        _overlay = new Grid();
        _ring = new Border { BorderBrush = TileChrome.Text, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(14), Margin = new Thickness(-6), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        var root = _root = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Children = { _board, _overlay, _ring } };
        root.MouseLeftButtonUp += (_, e) => { e.Handled = true; Click(); };
        Sync();
        return root;
    }

    private void Click()
    {
        if (!Gate.Armed)
        {
            // The window in front at the click; any other window in front disarms (the hook thread checks it on
            // every key, the WinEvent hook pauses the game).
            var desktop = ForegroundNative.Current();
            Gate.Arm(desktop);
            _focus ??= new ForegroundChangeHook(hwnd =>
            {
                if (hwnd != desktop && hwnd != 0) Application.Current?.Dispatcher.BeginInvoke(Release);
            });
            // A click anywhere outside the board (the desktop stays in front, so no foreground change) also pauses.
            var ui = Application.Current?.Dispatcher;
            _clickWatch = (x, y) => ui?.BeginInvoke(() => { if (!Inside(x, y)) Release(); });
            MouseHookNative.ButtonDown = _clickWatch;
        }
        _game.Click();
        Sync();
    }

    /// <summary>A key from the hook (UI thread), only while armed.</summary>
    public void OnKey(SnakeKey key)
    {
        if (!Gate.Armed) return;
        switch (key)
        {
            case SnakeKey.Up: _game.Turn(0, -1); Resume(); break;
            case SnakeKey.Down: _game.Turn(0, 1); Resume(); break;
            case SnakeKey.Left: _game.Turn(-1, 0); Resume(); break;
            case SnakeKey.Right: _game.Turn(1, 0); Resume(); break;
            case SnakeKey.Space: _game.Space(); break;
        }
        Sync();
    }

    /// <summary>Esc from the hook, or focus moved to another window: give the keys back and pause.</summary>
    public void Release()
    {
        Gate.Disarm();
        _focus?.Dispose();
        _focus = null;
        StopClickWatch();
        _game.Pause();
        Sync();
    }

    private void StopClickWatch()
    {
        if (_clickWatch is not null && MouseHookNative.ButtonDown == _clickWatch) MouseHookNative.ButtonDown = null;
        _clickWatch = null;
    }

    // Screen pixels against the board's on-screen rectangle.
    private bool Inside(int x, int y)
    {
        if (_root is null || PresentationSource.FromVisual(_root) is null) return false;
        var a = _root.PointToScreen(new Point(0, 0));
        var b = _root.PointToScreen(new Point(_root.ActualWidth, _root.ActualHeight));
        return x >= Math.Min(a.X, b.X) && x <= Math.Max(a.X, b.X) && y >= Math.Min(a.Y, b.Y) && y <= Math.Max(a.Y, b.Y);
    }

    private void Resume()
    {
        if (_game.State is SnakeState.Idle or SnakeState.Paused) _game.Click();
    }

    // Keeps the tick, the armed ring and the overlay in step with the game.
    private void Sync()
    {
        if (_game.State == SnakeState.Play) _tick.Start(); else _tick.Stop();
        if (_ring is not null) _ring.Visibility = Gate.Armed ? Visibility.Visible : Visibility.Collapsed;
        Draw();
    }

    private void Step()
    {
        _game.Step();
        if (_game.State == SnakeState.Over && _game.Best > _savedBest)
        {
            _savedBest = _game.Best;
            _saveBest(_savedBest);
        }
        Draw();
        if (_game.State != SnakeState.Play) _tick.Stop();
    }

    private void Draw()
    {
        _board?.InvalidateVisual();
        if (_overlay is null) return;
        _overlay.Children.Clear();
        _overlay.Background = _game.State is SnakeState.Idle or SnakeState.Paused or SnakeState.Over
            ? new SolidColorBrush(Color.FromArgb(0x8C, 0x0A, 0x0A, 0x0A)) : Brushes.Transparent;
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        switch (_game.State)
        {
            case SnakeState.Idle:
                center.Children.Add(Centered(TileChrome.Body("Snake", 18, FontWeights.SemiBold)));
                center.Children.Add(Centered(TileChrome.Label("Click · arrows / WASD", TileChrome.Frozen(0xCC, 0xCC, 0xCC)), 6));
                center.Children.Add(Centered(TileChrome.Label("Best " + _game.Best.ToString(CultureInfo.InvariantCulture)), 4));
                break;
            case SnakeState.Paused:
                center.Children.Add(Centered(TileChrome.Label("Paused · click to resume", TileChrome.Frozen(0xDD, 0xDD, 0xDD))));
                break;
            case SnakeState.Over:
                center.Children.Add(Centered(new DotMatrix { Text = _game.Score.ToString(CultureInfo.InvariantCulture), Dot = 5, Gap = 2, Fill = TileChrome.Text }));
                center.Children.Add(Centered(TileChrome.Label("Game over · space to retry", TileChrome.Frozen(0xDD, 0xDD, 0xDD)), 10));
                break;
            default:
                _overlay.Children.Add(new DotMatrix
                {
                    Text = _game.Score.ToString(CultureInfo.InvariantCulture), Dot = 2.4, Gap = 1, Fill = TileChrome.Text,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 2, 0),
                });
                return;
        }
        _overlay.Children.Add(center);
    }

    private static FrameworkElement Centered(FrameworkElement e, double top = 0)
    {
        e.HorizontalAlignment = HorizontalAlignment.Center;
        e.Margin = new Thickness(0, top, 0, 0);
        return e;
    }

    public void Dispose()
    {
        _tick.Stop();
        _focus?.Dispose();
        _focus = null;
        StopClickWatch();
        Gate.Disarm();
    }

    // The board: n×n dots (radius = pitch × 0.36); head a little larger; food red; dimmed body and red head when over.
    private sealed class Board(SnakeGame game) : FrameworkElement
    {
        protected override Size MeasureOverride(Size available)
        {
            var side = Math.Min(double.IsInfinity(available.Width) ? 252 : available.Width, double.IsInfinity(available.Height) ? 252 : available.Height);
            return new Size(side, side);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var n = game.Size;
            var side = Math.Min(ActualWidth, ActualHeight);
            var pitch = side / n;
            var r = pitch * 0.36;
            var ox = (ActualWidth - side) / 2;
            var oy = (ActualHeight - side) / 2;
            var body = game.Body.ToHashSet();
            var head = game.Body[0];
            var over = game.State == SnakeState.Over;
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    var p = (x, y);
                    Brush b = Unlit;
                    var rr = r;
                    if (body.Contains(p)) b = over ? Dead : TileChrome.Text;
                    if (p == head) { b = over ? TileChrome.Red : Brushes.White; if (!over) rr = r * 1.12; }
                    if (p == game.Food) b = TileChrome.Red;
                    dc.DrawEllipse(b, null, new Point(ox + x * pitch + pitch / 2, oy + y * pitch + pitch / 2), rr, rr);
                }
        }
    }
}
