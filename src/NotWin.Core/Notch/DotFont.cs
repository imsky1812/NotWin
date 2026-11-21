namespace NotWin.Core.Notch;

/// <summary>
/// The 5×7 dot-matrix font from the approved mockup (docs/mockups/final-v2.html, table <c>G</c>): digits plus
/// ':' '%' '°'. Numbers only, never sentences (spec §5). Glyphs are separated by one empty column.
/// </summary>
public static class DotFont
{
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
        ['3'] = ["11111", "00010", "00100", "00010", "00001", "10001", "01110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ['5'] = ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
        ['6'] = ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ['9'] = ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
        [':'] = ["0", "0", "1", "0", "1", "0", "0"],
        ['°'] = ["010", "101", "010", "000", "000", "000", "000"],
        ['%'] = ["11001", "11010", "00010", "00100", "01000", "01011", "10011"],
        ['-'] = ["00000", "00000", "00000", "11111", "00000", "00000", "00000"], // below-zero temperatures (Plan 4 weather)
        // Weather icons for the desktop weather widget (Plan 5): c, r, m from the mockup's G table; s, w, t, f drawn in
        // the same style. Letters are icons here, never text (spec §5: dots for numbers only).
        ['c'] = ["000000000", "000111000", "011111110", "111111111", "111111111", "011111110", "000000000"],
        ['r'] = ["000111000", "011111110", "111111111", "011111110", "000000000", "010101010", "101010100"],
        ['m'] = ["0011100", "0110000", "1100000", "1100000", "1100000", "0110000", "0011100"],
        ['s'] = ["0001000", "0100010", "0011100", "1011101", "0011100", "0100010", "0001000"],
        ['w'] = ["0001000", "1001001", "0101010", "0011100", "0101010", "1001001", "0001000"],
        ['t'] = ["000111000", "011111110", "111111111", "011111110", "000011000", "000110000", "000100000"],
        ['f'] = ["000000000", "111111110", "000000000", "011111111", "000000000", "111111110", "000000000"],
    };

    public const int Rows = 7;

    /// <summary>The glyph's 7 rows ('1' = lit), or null for a character the font does not have.</summary>
    public static string[]? GlyphRows(char c) => Glyphs.TryGetValue(c, out var g) ? g : null;

    /// <summary>Width in dot columns: glyph widths plus one gap column between glyphs (no trailing gap).</summary>
    public static int ColumnsFor(string text)
    {
        var cols = 0;
        var any = false;
        foreach (var c in text)
        {
            if (GlyphRows(c) is not { } g) continue;
            cols += g[0].Length + (any ? 1 : 0);
            any = true;
        }
        return cols;
    }

    /// <summary>Every lit dot as (column, row), left to right.</summary>
    public static IEnumerable<(int Col, int Row)> LitDots(string text)
    {
        var x = 0;
        foreach (var c in text)
        {
            if (GlyphRows(c) is not { } g) continue;
            for (var r = 0; r < Rows; r++)
                for (var col = 0; col < g[r].Length; col++)
                    if (g[r][col] == '1') yield return (x + col, r);
            x += g[0].Length + 1;
        }
    }
}
