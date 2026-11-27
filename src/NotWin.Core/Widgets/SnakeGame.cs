namespace NotWin.Core.Widgets;

public enum SnakeState { Idle, Play, Paused, Over }

public enum SnakeKey { Up, Down, Left, Right, Space }

/// <summary>
/// The snake widget's game (spec §6.5, mockup <c>snakeReset</c> / <c>snakeStep</c>): an n×n board (14 at 2×2, 20 at
/// 3×3), the snake starts length 3 at the centre moving right, food is placed off the body, walls and the snake's own
/// body end the game. Pure: the widget ticks <see cref="Step"/> every 120 ms while playing.
/// </summary>
public sealed class SnakeGame
{
    private readonly Random _rng;
    private readonly List<(int X, int Y)> _body = [];
    private (int X, int Y) _dir = (1, 0), _next = (1, 0);

    public SnakeGame(int size, Random rng, int best = 0)
    {
        _rng = rng;
        Best = best;
        Size = size;
        Reset();
    }

    /// <summary>The step time for a score: 120 ms, 3 ms quicker per point, never under 70 ms (Plan 13 follow-up).</summary>
    public static TimeSpan TickFor(int score) => TimeSpan.FromMilliseconds(Math.Max(70, 120 - 3 * Math.Max(0, score)));

    public int Size { get; private set; }
    public IReadOnlyList<(int X, int Y)> Body => _body;
    public (int X, int Y) Food { get; private set; }
    public int Score { get; private set; }
    public int Best { get; private set; }
    public SnakeState State { get; private set; } = SnakeState.Idle;

    /// <summary>The snake is drawn once a game has started (on the idle screen it would cover the title).</summary>
    public bool BodyVisible => State != SnakeState.Idle;

    /// <summary>Queues a turn; reversing onto the snake's own neck is ignored.</summary>
    public void Turn(int dx, int dy)
    {
        if (dx == -_dir.X && dy == -_dir.Y) return;
        _next = (dx, dy);
    }

    public void Step()
    {
        if (State != SnakeState.Play) return;
        _dir = _next;
        var (hx, hy) = _body[0];
        var head = (hx + _dir.X, hy + _dir.Y);
        if (head.Item1 < 0 || head.Item2 < 0 || head.Item1 >= Size || head.Item2 >= Size || _body.Contains(head))
        {
            State = SnakeState.Over;
            Best = Math.Max(Best, Score);
            return;
        }
        _body.Insert(0, head);
        if (head == Food)
        {
            Score++;
            PlaceFood();
        }
        else _body.RemoveAt(_body.Count - 1);
    }

    /// <summary>A click on the board: start or resume; after game over, a new game.</summary>
    public void Click()
    {
        if (State == SnakeState.Over) Reset();
        State = SnakeState.Play;
    }

    /// <summary>Space: pause / resume; after game over, a new game.</summary>
    public void Space()
    {
        switch (State)
        {
            case SnakeState.Over: Reset(); State = SnakeState.Play; break;
            case SnakeState.Play: State = SnakeState.Paused; break;
            default: State = SnakeState.Play; break;
        }
    }

    public void Pause()
    {
        if (State == SnakeState.Play) State = SnakeState.Paused;
    }

    /// <summary>A new board size (widget resized): a fresh game; a running one becomes paused.</summary>
    public void Resize(int size)
    {
        Size = size;
        var was = State;
        Reset();
        State = was is SnakeState.Play or SnakeState.Paused ? SnakeState.Paused : SnakeState.Idle;
    }

    private void Reset()
    {
        var c = Size / 2;
        _body.Clear();
        _body.AddRange([(c, c), (c - 1, c), (c - 2, c)]);
        _dir = _next = (1, 0);
        Score = 0;
        PlaceFood();
    }

    private void PlaceFood()
    {
        if (_body.Count >= Size * Size) return;
        (int, int) f;
        do f = (_rng.Next(Size), _rng.Next(Size));
        while (_body.Contains(f));
        Food = f;
    }
}

/// <summary>
/// Which keys go to the snake (Review Focus #1): only while armed by a click on the board — arrows, WASD and Space.
/// The widget disarms (and pauses) when focus moves anywhere else, so keystrokes are never eaten otherwise.
/// </summary>
public sealed class SnakeKeyGate
{
    private volatile bool _armed;
    private long _foreground;
    private readonly HashSet<int> _swallowedDowns = []; // hook thread only

    public bool Armed => _armed;

    /// <summary>Armed by a click; <paramref name="foreground"/> is the window in front at that click (0 = not checked).</summary>
    public void Arm(nint foreground = 0)
    {
        Interlocked.Exchange(ref _foreground, foreground);
        _armed = true;
    }

    public void Disarm() => _armed = false;

    /// <summary>
    /// The hook thread's decision for one key event: true = swallow. A key-down is swallowed only while armed, with
    /// no modifier held, for a snake key, and while the window that was in front at the arming click still is (any
    /// other window in front disarms right here, before the UI thread hears about it). A key-up is swallowed exactly
    /// when its key-down was, so no key is ever left "down" for other apps.
    /// </summary>
    public bool Filter(int vk, bool down, nint foreground, bool modifierDown)
    {
        if (!down) return _swallowedDowns.Remove(vk);
        if (!_armed) return false;
        var armedFor = (nint)Interlocked.Read(ref _foreground);
        if (armedFor != 0 && foreground != armedFor)
        {
            _armed = false;
            return false;
        }
        if (modifierDown || Map(vk) is null) return false;
        _swallowedDowns.Add(vk);
        return true;
    }

    public SnakeKey? Map(int vk) => vk switch
    {
        0x26 or 0x57 => SnakeKey.Up,      // Up, W
        0x28 or 0x53 => SnakeKey.Down,    // Down, S
        0x25 or 0x41 => SnakeKey.Left,    // Left, A
        0x27 or 0x44 => SnakeKey.Right,   // Right, D
        0x20 => SnakeKey.Space,
        _ => null,
    };

    public bool ShouldSwallow(int vk) => Armed && Map(vk) is not null;
}
