using NotWin.Core.Launcher;

namespace NotWin.Core.Tests.Launcher;

public sealed class AppIndexRulesTests
{
    [Theory]
    [InlineData("Uninstall Steam", @"C:\Program Files (x86)\Steam\uninstall.exe")]
    [InlineData("uninstall foo", "foo")]
    public void IsLaunchable_DropsUninstallers(string name, string parsing)
        => Assert.False(AppIndexRules.IsLaunchable(new AppsFolderEntry(parsing, name)));

    [Theory]
    [InlineData("Website", @"C:\x\site.url")]
    [InlineData("Manual", @"C:\x\manual.chm")]
    [InlineData("License", @"C:\x\license.TXT")]
    [InlineData("Guide", @"C:\x\guide.pdf")]
    [InlineData("Python 3.11 Manuals", @"C:\Python311\Doc\html\index.html")]
    [InlineData("Docs", @"C:\x\docs.htm")]
    [InlineData("Readme", @"C:\x\readme.exe")]
    [InlineData("Help", @"C:\x\help.exe")]
    [InlineData("Release Notes", @"C:\x\notes.exe")]
    public void IsLaunchable_DropsDocsAndUrls(string name, string parsing)
        => Assert.False(AppIndexRules.IsLaunchable(new AppsFolderEntry(parsing, name)));

    [Theory]
    [InlineData("Notepad", @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe")]
    [InlineData("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("Help Center Pro", @"C:\x\hcp.exe")]
    public void IsLaunchable_KeepsNormalApps(string name, string parsing)
        => Assert.True(AppIndexRules.IsLaunchable(new AppsFolderEntry(parsing, name)));

    [Fact]
    public void IsLaunchable_DropsBlankNames() => Assert.False(AppIndexRules.IsLaunchable(new AppsFolderEntry("x.exe", "  ")));
}
