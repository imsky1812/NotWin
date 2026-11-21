using NotWin.Core.Timing;

namespace NotWin.Core.Tests.Timing;

public sealed class TimerGridTests
{
    [Fact]
    public void Order_MatchesMockup()
    {
        Assert.Equal([24, 7, 23, 14, 11, 20, 3, 18, 17, 22, 21, 0, 8, 16, 4, 6, 19, 15, 1, 10, 9, 5, 2, 13, 12], TimerGrid.Order(5));
        Assert.Equal([32, 59, 43, 1, 27, 77, 91, 44], TimerGrid.Order(10).Take(8));
        Assert.Equal(Enumerable.Range(0, 100), TimerGrid.Order(10).Order());
    }

    [Fact]
    public void State_Quarter_LeavesQuarterAlive_NextIsFirstToGo()
    {
        var (alive, next) = TimerGrid.State(10, 0.25);
        Assert.Equal(25, alive.Count);
        Assert.Equal(TimerGrid.Order(10)[75], next);
        Assert.Contains(next!.Value, alive);
    }

    [Fact]
    public void State_Full_NoNext()
    {
        var (alive, next) = TimerGrid.State(5, 1);
        Assert.Equal(25, alive.Count);
        Assert.Null(next);
    }

    [Fact]
    public void State_Zero_NoneAlive()
    {
        var (alive, next) = TimerGrid.State(5, 0);
        Assert.Empty(alive);
        Assert.Null(next);
    }
}
