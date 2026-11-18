using NothingShell.Core;

namespace NothingShell.Core.Tests;

public sealed class PinRulesTests
{
    private const string Lnk = @"C:\pins\App.lnk";

    [Fact]
    public void Import_TargetExists_UsesLnkAndExeKey()
    {
        var p = PinRules.FromShortcut("Notepad", Lnk, @"C:\Windows\notepad.exe", targetExists: true, aumid: null);
        Assert.Equal(new PinnedApp(@"c:\windows\notepad.exe", "Notepad", Lnk, @"c:\windows\notepad.exe"), p);
    }

    [Fact]
    public void Import_StaleTargetWithAumid_IsKeptAndLaunchesThroughAppsFolder()
    {
        var p = PinRules.FromShortcut("Spotify", Lnk, @"C:\old\spotify.exe", targetExists: false, aumid: "Spotify.App");
        Assert.NotNull(p);
        Assert.Equal("Spotify.App", p.AppId);
        Assert.Equal(@"shell:AppsFolder\Spotify.App", p.LaunchPath);
        Assert.Null(p.TargetExe);
    }

    [Fact]
    public void Import_StaleTargetWithoutAumid_IsSkipped()
        => Assert.Null(PinRules.FromShortcut("Old", Lnk, @"C:\old\old.exe", targetExists: false, aumid: null));

    [Fact]
    public void Import_NoTargetButAumid_UsesLnk()
    {
        var p = PinRules.FromShortcut("Mail", Lnk, target: null, targetExists: false, aumid: "microsoft.windowscommunicationsapps!Mail");
        Assert.Equal(Lnk, p?.LaunchPath);
    }

    [Fact]
    public void Name_PrefersIndexedName()
        => Assert.Equal("WhatsApp", PinRules.Name("5319275A.WhatsAppDesktop!App", indexedName: "WhatsApp", exeDescription: "Application Frame Host", fallback: "WhatsApp Beta"));

    [Fact]
    public void Name_PackagedApp_NeverUsesHostExeDescription()
        => Assert.Equal("Calculator", PinRules.Name("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", indexedName: null, exeDescription: "Application Frame Host", fallback: "Calculator"));

    [Fact]
    public void Name_DesktopApp_UsesExeDescriptionThenFallback()
    {
        Assert.Equal("Google Chrome", PinRules.Name(@"c:\chrome\chrome.exe", null, "Google Chrome", "chrome"));
        Assert.Equal("chrome", PinRules.Name(@"c:\chrome\chrome.exe", null, "  ", "chrome"));
    }
}
