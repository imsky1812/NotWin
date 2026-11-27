using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NotWin.Core.Widgets;
using NotWin.Native;
using NotWin.Notch;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// Snake widget (spec §6.5, mockup <c>drawSnake</c>): playable on a 14×14 (2×2) or 20×20 (3×3) board. Click to take the
/// keys; arrows / WASD steer, Space pauses or restarts. Keys arrive through the shell's keyboard hook only while armed
/// (<see cref="SnakeKeyGate"/>); switching to any window, a click outside the board, or Esc disarms and pauses. White snake, red food, the score in
/// dots, the best score saved. The tick (120 ms, quicker as you score, never under 70 ms) runs only while playing.
/// </summary>
public sealed class SnakeWidget : IDesktopWidget
{
    private static readonly Brush Dead = TileChrome.Frozen(0x77, 0x77, 0x77);
    private static Brush Unlit => WidgetTheme.Unlit;
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
    private bool _newBest;

    /// <summary>The current step time (it shortens as the score grows).</summary>
    internal TimeSpan TickInterval => _tick.Interval;

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
        _ring = new Border { BorderBrush = WidgetTheme.Text, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(14), Margin = new Thickness(-6), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
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
        if (_game.State == SnakeState.Play)
        {
            if (_game.Score == 0) _newBest = false; // a fresh game
            _tick.Interval = SnakeGame.TickFor(_game.Score);
            _tick.Start();
        }
        else _tick.Stop();
        if (_ring is not null) _ring.Visibility = Gate.Armed ? Visibility.Visible : Visibility.Collapsed;
        Draw();
    }

    private void Step()
    {
        _game.Step();
        _tick.Interval = SnakeGame.TickFor(_game.Score); // speeds up as you score
        if (_board is not null) _board.Pulse++;
        if (_game.State == SnakeState.Over && _game.Best > _savedBest)
        {
            _newBest = true;
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
        _overlay.Background = _game.State is SnakeState.Idle or SnakeState.Paused or SnakeState.Over ? Shade : Brushes.Transparent;
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        switch (_game.State)
        {
            case SnakeState.Idle:
                center.Children.Add(Centered(WidgetTheme.Body("Snake", 18, FontWeights.SemiBold)));
                center.Children.Add(Centered(WidgetTheme.Label("Click · arrows / WASD", WidgetTheme.Soft), 6));
                center.Children.Add(Centered(WidgetTheme.Label("Best " + _game.Best.ToString(CultureInfo.InvariantCulture)), 4));
                break;
            case SnakeState.Paused:
                center.Children.Add(Centered(WidgetTheme.Label("Paused · click to resume", WidgetTheme.Soft)));
                break;
            case SnakeState.Over:
                DrawOver(center, _game.Score, _game.Best, _newBest);
                break;
            default:
                _overlay.Children.Add(new DotMatrix
                {
                    Text = _game.Score.ToString(CultureInfo.InvariantCulture), Dot = 2.4, Gap = 1, Fill = WidgetTheme.Text,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 2, 0),
                });
                return;
        }
        _overlay.Children.Add(center);
    }

    // A frosted shade over the board between games: white in light mode, black in dark mode (not a grey slab).
    private static Brush Shade => WidgetTheme.Light
        ? TileChrome.FrozenArgb(0xB3, 0xFF, 0xFF, 0xFF)
        : TileChrome.FrozenArgb(0x8C, 0x0A, 0x0A, 0x0A);

    private static void DrawOver(StackPanel center, int score, int best, bool newBest)
    {
        center.Children.Add(Centered(new DotMatrix { Text = score.ToString(CultureInfo.InvariantCulture), Dot = 5, Gap = 2, Fill = WidgetTheme.Text }));
        center.Children.Add(Centered(newBest
            ? WidgetTheme.Label("New best", TileChrome.Red)
            : WidgetTheme.Label("Best " + best.ToString(CultureInfo.InvariantCulture)), 10));
        center.Children.Add(Centered(WidgetTheme.Label("Space to retry", WidgetTheme.Soft), 4));
    }

    /// <summary>Shows the game-over card with these numbers (tests).</summary>
    internal void ShowOverForTest(int score, int best, bool newBest)
    {
        if (_overlay is null) return;
        _overlay.Children.Clear();
        _overlay.Background = Shade;
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        DrawOver(center, score, best, newBest);
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

    // The board (Plan 13 follow-up polish): small quiet empty dots so the snake stands out; the body fades towards the
    // tail; the head is a little larger; the food pulses gently; dimmed body and red head when the game is over.
    private sealed class Board(SnakeGame game) : FrameworkElement
    {
        /// <summary>Advanced every step: drives the food's pulse.</summary>
        public int Pulse { get; set; }

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
            var ox = (ActualWidth - side) / 2;
            var oy = (ActualHeight - side) / 2;
            Point At((int X, int Y) p) => new(ox + p.X * pitch + pitch / 2, oy + p.Y * pitch + pitch / 2);
            var over = game.State == SnakeState.Over;
            var body = game.BodyVisible ? game.Body : [];
            var taken = body.ToHashSet();
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                    if (!taken.Contains((x, y)) && (x, y) != game.Food)
                        dc.DrawEllipse(Unlit, null, At((x, y)), pitch * 0.2, pitch * 0.2);
            for (var i = body.Count - 1; i >= 0; i--)
            {
                var head = i == 0;
                var brush = over ? (head ? TileChrome.Red : Dead) : WidgetTheme.Text;
                var fade = body.Count <= 1 ? 1 : 1 - 0.55 * i / (body.Count - 1);
                dc.PushOpacity(head || over ? 1 : fade);
                dc.DrawEllipse(brush, null, At(body[i]), pitch * (head ? 0.44 : 0.38), pitch * (head ? 0.44 : 0.38));
                dc.Pop();
            }
            var pulse = 0.36 + 0.06 * Math.Sin(Pulse * 0.6);
            dc.DrawEllipse(TileChrome.Red, null, At(game.Food), pitch * pulse, pitch * pulse);
        }
    }
}
