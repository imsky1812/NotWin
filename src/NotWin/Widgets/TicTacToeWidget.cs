using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NotWin.Core.Widgets;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// Tic-tac-toe (Plan 9, user 2026-11-21): you are X (red dots), the shell is O (white dots), on a dotted 3×3 grid.
/// Click a square; the shell answers after 350 ms. At the end the winning line blinks; the next click starts a new
/// game, and who opens alternates. The score is saved.
/// </summary>
public sealed class TicTacToeWidget : IDesktopWidget
{
    private readonly TicTacToe _game = new();
    private readonly Random _rng = new();
    private readonly Func<GameScore> _score;
    private readonly Action<GameScore> _saveScore;
    private readonly DispatcherTimer _think = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private TicTacToeBoard? _board;
    private TextBlock? _status, _tally;
    private bool _shellOpensNext = true; // you open the first game
    private int _blinks;

    public TicTacToeWidget(Func<GameScore> score, Action<GameScore> saveScore)
    {
        _score = score;
        _saveScore = saveScore;
        _think.Tick += (_, _) => { _think.Stop(); ShellTurn(); };
        _blink.Tick += (_, _) =>
        {
            if (_board is null || ++_blinks > 6) { _blink.Stop(); if (_board is not null) _board.Dim = false; return; }
            _board.Dim = !_board.Dim;
        };
    }

    public WidgetKind Kind => WidgetKind.TicTacToe;

    public FrameworkElement Build(WidgetSize size)
    {
        _board = new TicTacToeBoard(_game) { Cursor = Cursors.Hand };
        _board.CellClicked += OnCell;
        _status = WidgetTheme.Label("");
        _status.Margin = new Thickness(0, 0, 0, 6);
        _tally = WidgetTheme.Label("");
        _tally.HorizontalAlignment = HorizontalAlignment.Center;
        _tally.Margin = new Thickness(0, 6, 0, 0);
        var panel = new DockPanel();
        DockPanel.SetDock(_status, System.Windows.Controls.Dock.Top);
        panel.Children.Add(_status);
        DockPanel.SetDock(_tally, System.Windows.Controls.Dock.Bottom);
        panel.Children.Add(_tally);
        panel.Children.Add(_board);
        Paint();
        return panel;
    }

    private void OnCell(int cell)
    {
        if (_think.IsEnabled) return; // the shell is thinking
        if (_game.Over)
        {
            _blink.Stop();
            if (_board is not null) _board.Dim = false;
            _game.Reset(_shellOpensNext, _rng);
            _shellOpensNext = !_shellOpensNext;
            Paint();
            return;
        }
        if (!_game.Play(cell)) return;
        Paint();
        if (_game.Over) Finished();
        else _think.Start();
    }

    private void ShellTurn()
    {
        _game.ShellMove(_rng);
        Paint();
        if (_game.Over) Finished();
    }

    private void Finished()
    {
        var s = _score();
        s = _game.Winner switch { 'X' => s with { You = s.You + 1 }, 'O' => s with { Shell = s.Shell + 1 }, _ => s with { Draws = s.Draws + 1 } };
        try { _saveScore(s); }
        catch (Exception e) { Log.Error("saving the tic-tac-toe score failed", e); }
        _blinks = 0;
        if (_game.Line is not null) _blink.Start();
        Paint();
    }

    private void Paint()
    {
        _board?.InvalidateVisual();
        if (_status is not null)
            _status.Text = !_game.Over ? (_think.IsEnabled ? "THINKING…" : "YOUR TURN")
                : _game.Winner switch { 'X' => "YOU WIN · TAP", 'O' => "NOTHING WINS · TAP", _ => "DRAW · TAP" };
        if (_tally is not null)
        {
            var s = _score();
            _tally.Text = string.Create(CultureInfo.InvariantCulture, $"YOU {s.You} · DRAW {s.Draws} · NOTHING {s.Shell}");
        }
    }

    public void Dispose()
    {
        _think.Stop();
        _blink.Stop();
    }
}

/// <summary>The board: dotted grid lines, X as a red dot cross, O as a white dot ring (5×5 dot pictures).</summary>
public sealed class TicTacToeBoard(TicTacToe game) : FrameworkElement
{
    private static readonly string[] XArt = ["#...#", ".#.#.", "..#..", ".#.#.", "#...#"];
    private static readonly string[] OArt = [".###.", "#...#", "#...#", "#...#", ".###."];
    private static Brush GridBrush => WidgetTheme.Unlit;
    private bool _dim;

    public event Action<int>? CellClicked;

    /// <summary>The winning line's blink: its marks drawn faint.</summary>
    public bool Dim
    {
        get => _dim;
        set { _dim = value; InvalidateVisual(); }
    }

    private (double Size, double Left, double Top) Square()
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        return (size, (ActualWidth - size) / 2, (ActualHeight - size) / 2);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var (size, left, top) = Square();
        if (size <= 0) return;
        var p = e.GetPosition(this);
        int col = (int)((p.X - left) / (size / 3)), row = (int)((p.Y - top) / (size / 3));
        if (col is < 0 or > 2 || row is < 0 or > 2) return;
        e.Handled = true;
        CellClicked?.Invoke(row * 3 + col);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var (size, left, top) = Square();
        if (size <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight)); // the whole area takes clicks
        var cell = size / 3;
        var r = Math.Max(1.2, cell / 26);
        // Dotted grid lines between the squares.
        for (var k = 1; k <= 2; k++)
            for (var t = cell / 8; t < size; t += cell / 4)
            {
                dc.DrawEllipse(GridBrush, null, new Point(left + k * cell, top + t), r * 0.8, r * 0.8);
                dc.DrawEllipse(GridBrush, null, new Point(left + t, top + k * cell), r * 0.8, r * 0.8);
            }
        var line = game.Line ?? [];
        for (var i = 0; i < 9; i++)
        {
            var c = game.Cells[i];
            if (c == '.') continue;
            var art = c == 'X' ? XArt : OArt;
            Brush brush = c == 'X' ? TileChrome.Red : WidgetTheme.Text;
            if (_dim && line.Contains(i)) brush = GridBrush;
            var pitch = cell * 0.56 / 4;
            double ox = left + (i % 3) * cell + cell / 2 - 2 * pitch, oy = top + (i / 3) * cell + cell / 2 - 2 * pitch;
            for (var y = 0; y < 5; y++)
                for (var x = 0; x < 5; x++)
                    if (art[y][x] == '#') dc.DrawEllipse(brush, null, new Point(ox + x * pitch, oy + y * pitch), r * 1.5, r * 1.5);
        }
    }
}
