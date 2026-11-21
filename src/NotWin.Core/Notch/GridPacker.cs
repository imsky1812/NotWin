namespace NotWin.Core.Notch;

/// <summary>
/// Dense packing of the notch dashboard (CSS <c>grid-auto-flow: dense</c>, mockup <c>.ngrid</c>): each tile goes in the
/// first free cell, scanning row by row and then column by column, where its whole W×H fits.
/// </summary>
public static class GridPacker
{
    public const int Columns = 8;

    public static IReadOnlyList<(int Col, int Row)> Pack(IReadOnlyList<TileSize> sizes, int columns = Columns)
    {
        var used = new List<bool[]>();
        var result = new List<(int, int)>(sizes.Count);
        foreach (var size in sizes)
        {
            var (w, h) = TileRules.Cells(size);
            w = Math.Min(w, columns);
            for (var row = 0; ; row++)
            {
                var col = FirstFit(used, row, w, h, columns);
                if (col < 0) continue;
                for (var r = row; r < row + h; r++)
                {
                    while (used.Count <= r) used.Add(new bool[columns]);
                    for (var c = col; c < col + w; c++) used[r][c] = true;
                }
                result.Add((col, row));
                break;
            }
        }
        return result;
    }

    public static int Rows(IReadOnlyList<TileSize> sizes, int columns = Columns)
    {
        var placed = Pack(sizes, columns);
        var rows = 0;
        for (var i = 0; i < placed.Count; i++)
            rows = Math.Max(rows, placed[i].Row + TileRules.Cells(sizes[i]).H);
        return rows;
    }

    private static int FirstFit(List<bool[]> used, int row, int w, int h, int columns)
    {
        for (var col = 0; col + w <= columns; col++)
        {
            var free = true;
            for (var r = row; r < row + h && free; r++)
                for (var c = col; c < col + w && free; c++)
                    if (r < used.Count && used[r][c]) free = false;
            if (free) return col;
        }
        return -1;
    }
}
