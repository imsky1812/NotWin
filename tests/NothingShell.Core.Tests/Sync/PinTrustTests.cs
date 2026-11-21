using NothingShell.Core.Sync;
using NothingShell.Core.Widgets;

namespace NothingShell.Core.Tests.Sync;

// Security review (2026-11-21) #4 / #5: a synced settings file never brings a launch path that is not where installed
// apps live, nor photo names or a legacy layout from another PC.
public sealed class PinTrustTests
{
    private static readonly string[] Roots = [@"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows",
        @"C:\Users\me\AppData\Local\Programs", @"C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu"];

    [Theory]
    [InlineData(@"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", true)]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe", true)]
    [InlineData(@"C:\Users\me\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Opera GX.lnk", true)]
    [InlineData(@"C:\Users\me\AppData\Local\Programs\Opera GX\opera.exe", true)]
    [InlineData(@"\\attacker\share\x.lnk", false)]
    [InlineData(@"D:\dropped\evil.exe", false)]
    [InlineData(@"C:\Users\me\OneDrive\Shared\evil.exe", false)]
    [InlineData(@"C:\Program Files\..\Users\me\evil.exe", false)]
    [InlineData(@"C:\Program FilesX\evil.exe", false)]
    [InlineData(@"relative\evil.exe", false)]
    [InlineData(@"shell:AppsFolder\..\..\x", false)]
    [InlineData("", false)]
    public void IsTrusted_OnlyWhereInstalledAppsLive(string path, bool trusted) =>
        Assert.Equal(trusted, PinTrust.IsTrusted(path, Roots));

    [Fact]
    public void Merge_TakesOwnPinOrTrustedPin_DropsTheRest()
    {
        var mine = new ShellSettings { PinnedApps = [new PinnedApp("chrome", "Chrome", @"C:\Program Files\Chrome\chrome.exe")] };
        var theirs = new ShellSettings
        {
            PinnedApps =
            [
                new PinnedApp("chrome", "Chrome", @"\\attacker\share\chrome.lnk"),               // ours wins
                new PinnedApp("spotify", "Spotify", @"shell:AppsFolder\Spotify!App"),             // trusted
                new PinnedApp("evil", "Notepad", @"D:\dropped\evil.exe"),                         // dropped
                new PinnedApp("half", "Half", @"C:\Program Files\x.exe", @"\\attacker\x.exe"),   // target untrusted: dropped
            ],
        };
        var merged = SyncRule.Merge(mine, theirs, Roots).PinnedApps;
        Assert.Equal(["chrome", "spotify"], merged.Select(p => p.AppId));
        Assert.Equal(@"C:\Program Files\Chrome\chrome.exe", merged[0].LaunchPath);
    }

    [Fact]
    public void Merge_DropsTheLegacyLayoutFromTheOtherPc()
    {
        var mine = new ShellSettings { Widgets = null };
        var theirs = new ShellSettings
        {
            WidgetList = null,
            Widgets = new DesktopLayout([new(WidgetKind.Photo, new(2, 2), 0, 0, "f1", new(Photos: [@"\\attacker\s\a.jpg"]))]),
        };
        var merged = SyncRule.Merge(mine, theirs, Roots);
        Assert.Null(merged.Widgets);
    }

    [Fact]
    public void Sanitize_ReplacesUnsafeIds()
    {
        var bad = new DesktopLayout([new(WidgetKind.Photo, new(2, 2), 0, 0, @"..\..\Windows"), new(WidgetKind.Clock, new(3, 1), 296, 0, "ok12")]);
        var clean = DesktopLayout.Sanitize(bad, 1600, 900, () => "fresh001");
        Assert.Equal(["fresh001", "ok12"], clean.Widgets.Select(p => p.Id));
    }
}
