using NotWin.Core.Widgets;

namespace NotWin.Core.Tests.Widgets;

// Plan 9: you (X) against the shell (O). The shell takes a win, blocks yours, else centre, a corner, any square.
public sealed class TicTacToeTests
{
    private static TicTacToe Board(string cells)
    {
        var g = new TicTacToe();
        g.Load(cells);
        return g;
    }

    [Theory]
    [InlineData("XXX......", 'X', new[] { 0, 1, 2 })]
    [InlineData("O..O..O..", 'O', new[] { 0, 3, 6 })]
    [InlineData("X...X...X", 'X', new[] { 0, 4, 8 })]
    [InlineData("..O.O.O..", 'O', new[] { 2, 4, 6 })]
    public void Winner_RowColumnDiagonal(string cells, char winner, int[] line)
    {
        var g = Board(cells);
        Assert.True(g.Over);
        Assert.Equal(winner, g.Winner);
        Assert.Equal(line, g.Line);
    }

    [Fact]
    public void Play_TakenOrOver_IsRefused()
    {
        var g = Board("O........");
        Assert.False(g.Play(0));
        Assert.True(g.Play(4));
        Assert.Equal('X', g.Cells[4]);
        var done = Board("XXX.OO...");
        Assert.False(done.Play(8));
        Assert.Equal('.', done.Cells[8]);
    }

    [Fact]
    public void ShellMove_TakesTheWin()
    {
        var g = Board("XX.OO.X..");
        g.ShellMove(new Random(1));
        Assert.Equal('O', g.Cells[5]);
        Assert.Equal('O', g.Winner);
    }

    [Fact]
    public void ShellMove_BlocksYourWin()
    {
        var g = Board("XX..O....");
        g.ShellMove(new Random(1));
        Assert.Equal('O', g.Cells[2]);
    }

    [Fact]
    public void ShellMove_EmptyBoard_TakesCentre()
    {
        var g = new TicTacToe();
        g.ShellMove(new Random(1));
        Assert.Equal('O', g.Cells[4]);
    }

    [Fact]
    public void ShellMove_CentreTaken_TakesACorner()
    {
        var g = Board("....X....");
        g.ShellMove(new Random(7));
        Assert.Contains(Array.IndexOf(g.Cells, 'O'), new[] { 0, 2, 6, 8 });
    }

    [Fact]
    public void ShellMove_FullBoard_DoesNothing()
    {
        var g = Board("XOXXOOOXX");
        g.ShellMove(new Random(1));
        Assert.Equal("XOXXOOOXX", new string(g.Cells));
    }

    [Fact]
    public void Draw_WhenFullWithNoLine()
    {
        var g = Board("XOXXOOOXX");
        Assert.True(g.Over);
        Assert.Null(g.Winner);
        Assert.Null(g.Line);
    }

    [Fact]
    public void Reset_ShellFirst_PlaysOneMove()
    {
        var g = Board("XO.......");
        g.Reset(shellFirst: true, new Random(1));
        Assert.Equal(1, g.Cells.Count(c => c == 'O'));
        Assert.Equal(0, g.Cells.Count(c => c == 'X'));
        g.Reset(shellFirst: false, new Random(1));
        Assert.All(g.Cells, c => Assert.Equal('.', c));
    }

    [Fact]
    public void Rules_SizesAndPicker()
    {
        Assert.Equal([new WidgetSize(2, 2), new WidgetSize(3, 3)], WidgetRules.Sizes(WidgetKind.TicTacToe));
        Assert.Equal(10, (int)WidgetKind.TicTacToe);
        var order = WidgetRules.PickerOrder.ToList();
        Assert.Equal(order.IndexOf(WidgetKind.Snake) + 1, order.IndexOf(WidgetKind.TicTacToe));
        Assert.False(WidgetRules.IsRepeatable(WidgetKind.TicTacToe));
    }
}
