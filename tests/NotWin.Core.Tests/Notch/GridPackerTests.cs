using NotWin.Core.Notch;
using static NotWin.Core.Notch.TileSize;

namespace NotWin.Core.Tests.Notch;

public sealed class GridPackerTests
{
    [Fact]
    public void Pack_FourBigTiles_TwoRows()
    {
        TileSize[] sizes = [Big, Big, Big, Big];
        Assert.Equal([(0, 0), (2, 0), (4, 0), (6, 0)], GridPacker.Pack(sizes));
        Assert.Equal(2, GridPacker.Rows(sizes));
    }

    [Fact]
    public void Pack_Page2_FitsTwoRows()
    {
        var sizes = DashboardLayout.Default.Pages[1].Select(t => t.Size).ToList();
        Assert.Equal(2, GridPacker.Rows(sizes));
    }

    [Fact]
    public void Pack_Dense_FillsHoleWithLaterOne()
    {
        TileSize[] sizes = [Big, Wide, Wide, Wide, Big, One];
        Assert.Equal([(0, 0), (2, 0), (4, 0), (6, 0), (2, 1), (4, 1)], GridPacker.Pack(sizes));
        Assert.Equal(3, GridPacker.Rows(sizes));
    }

    [Fact]
    public void Rows_Empty_Zero() => Assert.Equal(0, GridPacker.Rows([]));
}
