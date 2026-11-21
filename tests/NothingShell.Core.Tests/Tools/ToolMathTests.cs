using NothingShell.Core.Tools;

namespace NothingShell.Core.Tests.Tools;

public sealed class ToolMathTests
{
    private static readonly RectI Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void Normalize_AnyDirection()
    {
        Assert.Equal(new RectI(100, 50, 300, 200), ToolMath.Normalize(new PointI(300, 200), new PointI(100, 50), Screen));
        Assert.Equal(new RectI(100, 50, 300, 200), ToolMath.Normalize(new PointI(100, 200), new PointI(300, 50), Screen));
    }

    [Fact]
    public void Normalize_Tiny_Null()
    {
        Assert.Null(ToolMath.Normalize(new PointI(10, 10), new PointI(13, 40), Screen)); // 3 px wide
        Assert.NotNull(ToolMath.Normalize(new PointI(10, 10), new PointI(14, 14), Screen));
    }

    [Fact]
    public void Normalize_ClampsToBounds() =>
        Assert.Equal(new RectI(0, 1000, 50, 1080), ToolMath.Normalize(new PointI(-20, 1000), new PointI(50, 1200), Screen));

    [Fact]
    public void Hex_Formats() => Assert.Equal("#E8231F", ToolMath.Hex(0xE8, 0x23, 0x1F));

    [Fact]
    public void SnipFileName_Format() =>
        Assert.Equal("Nothing Shell 2026-11-19 143005.png", ToolMath.SnipFileName(new DateTime(2026, 11, 19, 14, 30, 5)));

    [Fact]
    public void JoinOcrLines_DropsBlank() =>
        Assert.Equal("Hello world\nSecond", ToolMath.JoinOcrLines(["Hello world", "  ", "", "Second "]));

    [Fact]
    public void WordCount_Counts()
    {
        Assert.Equal(3, ToolMath.WordCount("one two\nthree"));
        Assert.Equal(0, ToolMath.WordCount("   "));
    }
}
