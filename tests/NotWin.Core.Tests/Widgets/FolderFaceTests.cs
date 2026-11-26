using NotWin.Core.Widgets;

namespace NotWin.Core.Tests.Widgets;

/// <summary>Folder icon "Peek + name" (user 2026-11-27): the apps inside as icons, a name and a count.</summary>
public sealed class FolderFaceTests
{
    [Theory]
    [InlineData(0, false, 0, 0)]
    [InlineData(2, false, 2, 0)]
    [InlineData(3, false, 3, 0)]
    [InlineData(4, false, 2, 2)]   // two icons and a "+2" circle
    [InlineData(9, false, 2, 7)]
    [InlineData(6, true, 6, 0)]
    [InlineData(7, true, 5, 2)]    // five icons and a "+2" circle
    public void Slots(int apps, bool big, int shown, int more) => Assert.Equal((shown, more), FolderFace.Slots(apps, big));

    [Fact]
    public void CountLabel()
    {
        Assert.Equal("ADD APPS", FolderFace.CountLabel(0));
        Assert.Equal("1 APP", FolderFace.CountLabel(1));
        Assert.Equal("6 APPS", FolderFace.CountLabel(6));
    }

    [Fact]
    public void Name_DefaultsAndTrims()
    {
        Assert.Equal("FOLDER", FolderFace.Name(null));
        Assert.Equal("FOLDER", FolderFace.Name("   "));
        Assert.Equal("WORK", FolderFace.Name("  work "));
        Assert.Equal(24, FolderFace.Name(new string('x', 60)).Length);
    }

    [Fact]
    public void Config_KeepsTheTitle() =>
        Assert.Equal("Games", (new WidgetConfig(Title: "Games") with { Apps = ["a"] }).Title);
}
