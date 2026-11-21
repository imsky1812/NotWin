using NotWin.Core.Launcher;

namespace NotWin.Core.Tests.Launcher;

public sealed class SettingsPagesTests
{
    [Fact]
    public void All_HasUniqueIdsAndMsSettingsUris()
    {
        Assert.All(SettingsPages.All, p =>
        {
            Assert.StartsWith("ms-settings:", p.Id);
            Assert.Equal(p.Id, p.Target);
            Assert.Equal(LaunchKind.Setting, p.Kind);
        });
        Assert.Equal(SettingsPages.All.Count, SettingsPages.All.Select(p => p.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("Display")]
    [InlineData("Sound")]
    [InlineData("Bluetooth & devices")]
    [InlineData("Wi-Fi")]
    [InlineData("Notifications")]
    [InlineData("Windows Update")]
    public void All_ContainsCorePages(string name) => Assert.Contains(SettingsPages.All, p => p.Name == name);
}
