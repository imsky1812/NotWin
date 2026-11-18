using NothingShell.Core.Panel;

namespace NothingShell.Core.Tests.Panel;

public sealed class WifiRulesTests
{
    private static WifiNetwork W(string ssid, int signal, bool secured = true, bool profile = false, bool connected = false)
        => new(ssid, signal, secured, profile, connected);

    [Fact]
    public void Order_ConnectedThenKnownThenSignal()
    {
        var r = WifiRules.Order([W("cafe", 90), W("home", 40, profile: true, connected: true), W("work", 60, profile: true), W("lab", 95)]);
        Assert.Equal(["home", "work", "lab", "cafe"], r.Select(n => n.Ssid));
    }

    [Fact]
    public void Order_MergesDuplicateSsids()
    {
        var r = WifiRules.Order([W("home", 40), W("home", 80, profile: true), W("cafe", 50)]);
        var home = Assert.Single(r, n => n.Ssid == "home");
        Assert.Equal(80, home.SignalPercent);
        Assert.True(home.HasProfile);
    }

    [Fact]
    public void Order_DropsHiddenEmptySsid() => Assert.Empty(WifiRules.Order([W("", 90), W("  ", 50)]));

    [Fact] public void Action_Connected_None() => Assert.Equal(WifiAction.None, WifiRules.ActionFor(W("a", 50, profile: true, connected: true)));
    [Fact] public void Action_Known_Connect() => Assert.Equal(WifiAction.Connect, WifiRules.ActionFor(W("a", 50, profile: true)));
    [Fact] public void Action_OpenUnknown_ConnectWithNewOpenProfile() => Assert.Equal(WifiAction.ConnectWithNewOpenProfile, WifiRules.ActionFor(W("a", 50, secured: false)));
    [Fact] public void Action_SecuredUnknown_OpensSettings() => Assert.Equal(WifiAction.OpenSettings, WifiRules.ActionFor(W("a", 50, secured: true)));

    [Fact]
    public void Classify_AccessDenied_ShowsLocationHint() => Assert.Contains("location", WifiRules.Hint(WifiScanStatus.LocationDenied)!, StringComparison.OrdinalIgnoreCase);

    [Fact] public void Hint_RadioOff() => Assert.NotNull(WifiRules.Hint(WifiScanStatus.RadioOff));
    [Fact] public void Hint_Ok_IsNull() => Assert.Null(WifiRules.Hint(WifiScanStatus.Ok));

    [Theory]
    [InlineData(null, false, null, "Unavailable")]
    [InlineData(false, false, null, "Off")]
    [InlineData(true, false, null, "Not connected")]
    [InlineData(true, true, null, "Connected")]       // connected, SSID withheld (no location consent): never "not connected"
    [InlineData(true, true, "home", "home")]
    public void TileSubline_NeverNeedsAScan(bool? radioOn, bool connected, string? ssid, string expected)
        => Assert.Equal(expected, WifiRules.TileSubline(radioOn, connected, ssid));

    [Fact]
    public void OpenProfileXml_EscapesSsid()
    {
        var xml = WifiRules.OpenProfileXml("Tom & Jerry's <Wi-Fi>");
        Assert.Contains("<name>Tom &amp; Jerry&apos;s &lt;Wi-Fi&gt;</name>", xml);
        Assert.Contains("<authentication>open</authentication>", xml);
        Assert.Contains("<encryption>none</encryption>", xml);
    }
}
