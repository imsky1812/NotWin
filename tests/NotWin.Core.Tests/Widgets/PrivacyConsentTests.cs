using NotWin.Core.Widgets;

namespace NotWin.Core.Tests.Widgets;

public sealed class PrivacyConsentTests
{
    [Theory]
    [InlineData("Allow", "Allow", "Allow", ToggleState.On)]
    [InlineData(null, null, null, ToggleState.On)]
    [InlineData("Allow", "Deny", "Allow", ToggleState.Off)]
    [InlineData("Allow", "Allow", "Deny", ToggleState.Off)]
    [InlineData("Deny", "Allow", "Allow", ToggleState.Blocked)]
    [InlineData("Deny", "Deny", "Deny", ToggleState.Blocked)]
    public void State(string? machine, string? user, string? nonPackaged, ToggleState expected) =>
        Assert.Equal(expected, PrivacyConsent.State(machine, user, nonPackaged));

    [Fact]
    public void CanChange()
    {
        Assert.False(PrivacyConsent.CanChange("Deny"));
        Assert.True(PrivacyConsent.CanChange("Allow"));
        Assert.True(PrivacyConsent.CanChange(null));
    }
}
