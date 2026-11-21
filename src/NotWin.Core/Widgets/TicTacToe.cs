namespace NotWin.Core.Widgets;

/// <summary>The tic-tac-toe widget's running score (saved in settings).</summary>
public sealed record GameScore(int You = 0, int Draws = 0, int Shell = 0);

/// <summary>
/// Tic-tac-toe (Plan 9, user 2026-11-21): you are X, the shell is O. The shell takes a win when it can, blocks yours,
/// else takes the centre, a corner, then any square (random among equals): beatable, not easily.
/// </summary>
public sealed class TicTacToe
{
    private static readonly int[][] Lines =
    [
        [0, 1, 2], [3, 4, 5], [6, 7, 8], [0, 3, 6], [1, 4, 7], [2, 5, 8], [0, 4, 8], [2, 4, 6],
    ];

    public char[] Cells { get; } = ".........".ToCharArray();
    public char? Winner { get; private set; }
    public int[]? Line { get; private set; }
    public bool Over => Winner is not null || Array.IndexOf(Cells, '.') < 0;

    /// <summary>Sets the board from nine characters of '.', 'X', 'O' (tests, and restoring a game).</summary>
    public void Load(string cells)
    {
        if (cells.Length != 9 || cells.Any(c => c is not ('.' or 'X' or 'O'))) throw new ArgumentException("nine of . X O", nameof(cells));
        cells.CopyTo(0, Cells, 0, 9);
        Judge();
    }

    /// <summary>Your move (X) on <paramref name="cell"/> 0..8; false when it is taken or the game is over.</summary>
    public bool Play(int cell)
    {
        if (Over || cell is < 0 or > 8 || Cells[cell] != '.') return false;
        Cells[cell] = 'X';
        Judge();
        return true;
    }

    /// <summary>The shell's move (O); nothing when the game is over.</summary>
    public void ShellMove(Random rng)
    {
        if (Over) return;
        var cell = Completing('O') ?? Completing('X') ?? (Cells[4] == '.' ? 4 : (int?)null)
            ?? Pick([0, 2, 6, 8], rng) ?? Pick([1, 3, 5, 7], rng);
        if (cell is not { } c) return;
        Cells[c] = 'O';
        Judge();
    }

    /// <summary>A new game; the shell opens when <paramref name="shellFirst"/>.</summary>
    public void Reset(bool shellFirst, Random rng)
    {
        Array.Fill(Cells, '.');
        Winner = null;
        Line = null;
        if (shellFirst) ShellMove(rng);
    }

    // The empty square that completes a line of two <paramref name="who"/>.
    private int? Completing(char who)
    {
        foreach (var l in Lines)
        {
            var mine = l.Count(i => Cells[i] == who);
            var empty = l.Where(i => Cells[i] == '.').ToList();
            if (mine == 2 && empty.Count == 1) return empty[0];
        }
        return null;
    }

    private int? Pick(int[] choices, Random rng)
    {
        var free = choices.Where(i => Cells[i] == '.').ToList();
        return free.Count == 0 ? null : free[rng.Next(free.Count)];
    }

    private void Judge()
    {
        Winner = null;
        Line = null;
        foreach (var l in Lines)
        {
            var c = Cells[l[0]];
            if (c != '.' && Cells[l[1]] == c && Cells[l[2]] == c)
            {
                Winner = c;
                Line = l;
                return;
            }
        }
    }
}
