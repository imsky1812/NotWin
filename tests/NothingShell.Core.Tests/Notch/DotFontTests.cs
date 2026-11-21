using NothingShell.Core.Notch;

namespace NothingShell.Core.Tests.Notch;

public sealed class DotFontTests
{
    [Fact]
    public void Digits_AreFiveWide_SevenHigh()
    {
        foreach (var c in "0123456789")
        {
            var rows = DotFont.GlyphRows(c)!;
            Assert.Equal(7, rows.Length);
            Assert.All(rows, r => Assert.Equal(5, r.Length));
        }
    }

    [Fact]
    public void Colon_IsOneWide() =>
        Assert.Equal(["0", "0", "1", "0", "1", "0", "0"], DotFont.GlyphRows(':')!);

    [Fact]
    public void Columns_For_1234WithColon() => Assert.Equal(25, DotFont.ColumnsFor("12:34"));

    [Fact]
    public void Unknown_IsSkipped()
    {
        Assert.Null(DotFont.GlyphRows('x'));
        Assert.Equal(DotFont.ColumnsFor("12"), DotFont.ColumnsFor("1x2"));
    }

    [Fact]
    public void Glyph_Zero_MatchesMockup() =>
        Assert.Equal(["01110", "10001", "10001", "10001", "10001", "10001", "01110"], DotFont.GlyphRows('0')!);

    [Fact]
    public void LitDots_PlaceSecondGlyphAfterOneGapColumn()
    {
        // "11": the second '1' starts at column 6 (5 wide + 1 gap). '1' row 0 is "00100" -> column 2 lit.
        var dots = DotFont.LitDots("11").ToList();
        Assert.Contains((2, 0), dots);
        Assert.Contains((8, 0), dots);
        Assert.Equal(2 * 10, dots.Count); // '1' has 10 lit dots: 1+2+1+1+1+1+3
    }

    [Fact]
    public void Empty_IsZeroColumns() => Assert.Equal(0, DotFont.ColumnsFor(""));
}
