using NothingShell.Core.Widgets;

namespace NothingShell.Core.Tests.Widgets;

public sealed class SnakeGameTests
{
    private static SnakeGame Playing(int size = 20, int seed = 3)
    {
        var g = new SnakeGame(size, new Random(seed));
        g.Click();
        return g;
    }

    // Steers to the food and eats it (never reverses; goes round when the food is behind).
    private static void EatOnce(SnakeGame g)
    {
        var score = g.Score;
        for (var i = 0; i < 400 && g.Score == score && g.State == SnakeState.Play; i++)
        {
            var (hx, hy) = g.Body[0];
            var (fx, fy) = g.Food;
            var dir = (hx - g.Body[1].X, hy - g.Body[1].Y);
            (int X, int Y) want = fx > hx ? (1, 0) : fx < hx ? (-1, 0) : fy > hy ? (0, 1) : (0, -1);
            if (want == (-dir.Item1, -dir.Item2))
                want = dir.Item1 != 0 ? (0, hy < g.Size / 2 ? 1 : -1) : (hx < g.Size / 2 ? 1 : -1, 0); // go round, not back
            g.Turn(want.X, want.Y);
            g.Step();
        }
        Assert.Equal(score + 1, g.Score);
    }

    [Fact]
    public void Start_CentredMovingRight_Length3()
    {
        var g = new SnakeGame(14, new Random(1));
        Assert.Equal([(7, 7), (6, 7), (5, 7)], g.Body);
        Assert.Equal(SnakeState.Idle, g.State);
        Assert.DoesNotContain(g.Food, g.Body);
    }

    [Fact]
    public void Step_MovesHead_OnlyWhilePlaying()
    {
        var g = new SnakeGame(14, new Random(1));
        g.Step();
        Assert.Equal((7, 7), g.Body[0]); // idle: nothing moves
        g.Click();
        g.Step();
        Assert.Equal((8, 7), g.Body[0]);
        Assert.Equal(3, g.Body.Count);
    }

    [Fact]
    public void Turn_ReverseIgnored()
    {
        var g = Playing(14);
        g.Turn(-1, 0);
        g.Step();
        Assert.Equal((8, 7), g.Body[0]);
        g.Turn(0, -1);
        g.Step();
        Assert.Equal((8, 6), g.Body[0]);
    }

    [Fact]
    public void Eat_GrowsAndScores_FoodNotOnBody()
    {
        var g = Playing();
        EatOnce(g);
        Assert.Equal(4, g.Body.Count);
        Assert.DoesNotContain(g.Food, g.Body);
    }

    [Fact]
    public void Wall_EndsGame_UpdatesBest()
    {
        var g = Playing();
        EatOnce(g);
        g.Turn(1, 0);
        for (var i = 0; i < 40 && g.State == SnakeState.Play; i++) { g.Turn(1, 0); g.Step(); }
        Assert.Equal(SnakeState.Over, g.State);
        Assert.Equal(1, g.Best);
    }

    [Fact]
    public void Self_EndsGame()
    {
        var g = Playing();
        EatOnce(g);
        EatOnce(g); // length 5: a tight U-turn now bites the body
        var (dx, dy) = (g.Body[0].X - g.Body[1].X, g.Body[0].Y - g.Body[1].Y);
        // Turn left of the current direction three times: a square loop back into the neck.
        (int, int) Left((int X, int Y) d) => (d.Y, -d.X);
        var d = (dx, dy);
        for (var i = 0; i < 3 && g.State == SnakeState.Play; i++)
        {
            d = Left(d);
            g.Turn(d.Item1, d.Item2);
            g.Step();
        }
        Assert.Equal(SnakeState.Over, g.State);
    }

    [Fact]
    public void Space_TogglesPause_AndRestartsAfterOver()
    {
        var g = Playing(14);
        g.Space();
        Assert.Equal(SnakeState.Paused, g.State);
        g.Space();
        Assert.Equal(SnakeState.Play, g.State);
        for (var i = 0; i < 20 && g.State == SnakeState.Play; i++) g.Step(); // into the right wall
        Assert.Equal(SnakeState.Over, g.State);
        g.Space();
        Assert.Equal(SnakeState.Play, g.State);
        Assert.Equal(3, g.Body.Count);
        Assert.Equal(0, g.Score);
    }

    [Fact]
    public void Resize_ResetsAndPauses()
    {
        var g = Playing(14);
        g.Step();
        g.Resize(20);
        Assert.Equal(20, g.Size);
        Assert.Equal((10, 10), g.Body[0]);
        Assert.Equal(SnakeState.Paused, g.State);
        var idle = new SnakeGame(14, new Random(1));
        idle.Resize(20);
        Assert.Equal(SnakeState.Idle, idle.State);
    }

    [Fact]
    public void Best_StartsFromSaved()
    {
        var g = new SnakeGame(14, new Random(1), best: 9);
        Assert.Equal(9, g.Best);
    }

    [Fact]
    public void Gate_OnlyArmedSwallows()
    {
        var gate = new SnakeKeyGate();
        Assert.False(gate.ShouldSwallow(0x26)); // Up, unarmed
        gate.Arm();
        foreach (var vk in new[] { 0x26, 0x28, 0x25, 0x27, 0x57, 0x41, 0x53, 0x44, 0x20 }) Assert.True(gate.ShouldSwallow(vk));
        Assert.False(gate.ShouldSwallow(0x0D)); // Enter
        Assert.False(gate.ShouldSwallow(0x45)); // E
        Assert.Equal(SnakeKey.Left, gate.Map(0x41));
        Assert.Equal(SnakeKey.Space, gate.Map(0x20));
        Assert.Null(gate.Map(0x0D));
    }

    [Fact]
    public void Gate_ForegroundChange_DisarmsAndPauses()
    {
        var gate = new SnakeKeyGate();
        gate.Arm();
        gate.Disarm(); // what the widget does when focus leaves (and it pauses the game)
        Assert.False(gate.ShouldSwallow(0x26));
        Assert.False(gate.Armed);
    }

    [Fact]
    public void Gate_Filter_SwallowsOnlyUpsOfSwallowedDowns()
    {
        var gate = new SnakeKeyGate();
        gate.Arm(100);
        Assert.False(gate.Filter(0x41, down: true, foreground: 100, modifierDown: true)); // Ctrl+A passes
        Assert.False(gate.Filter(0x41, down: false, foreground: 100, modifierDown: false)); // its key-up passes too
        Assert.True(gate.Filter(0x26, down: true, foreground: 100, modifierDown: false));
        gate.Disarm();
        Assert.True(gate.Filter(0x26, down: false, foreground: 100, modifierDown: false)); // paired up still swallowed
        Assert.False(gate.Filter(0x26, down: false, foreground: 100, modifierDown: false)); // only once
    }

    [Fact]
    public void Gate_Filter_ForegroundMoved_DisarmsOnTheHookThread()
    {
        var gate = new SnakeKeyGate();
        gate.Arm(100);
        Assert.False(gate.Filter(0x57, down: true, foreground: 200, modifierDown: false)); // W reaches the new window
        Assert.False(gate.Armed);
        Assert.False(gate.Filter(0x57, down: false, foreground: 200, modifierDown: false));
    }
}
