namespace NotWin.Core.Tests;

public sealed class PackagedAppsTests
{
    [Fact]
    public void FromExePath_StoreApp_GivesRootFamilyAndRelativeExe()
    {
        var p = PackagedApps.FromExePath(@"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.309.2055.0_x64__zpdnekdrzrea0\Spotify.exe");
        Assert.NotNull(p);
        Assert.Equal(@"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.309.2055.0_x64__zpdnekdrzrea0", p.Value.Root);
        Assert.Equal("SpotifyAB.SpotifyMusic_zpdnekdrzrea0", p.Value.Family);
        Assert.Equal("Spotify.exe", p.Value.RelativeExe);
    }

    [Fact]
    public void FromExePath_NestedExe_AndLowerCasePath()
    {
        var p = PackagedApps.FromExePath(@"c:\program files\windowsapps\claude_6.6395.0.0_x64__pzs8sxrjxfjjc\app\claude.exe");
        Assert.Equal("claude_pzs8sxrjxfjjc", p!.Value.Family);
        Assert.Equal(@"app\claude.exe", p.Value.RelativeExe);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\Spotify.exe")]              // no package folder
    [InlineData(@"C:\Program Files\WindowsApps\NoPublisherId_1.0_x64\a.exe")] // no "__publisherId"
    [InlineData("")]
    public void FromExePath_NotAStorePackage_IsNull(string path) => Assert.Null(PackagedApps.FromExePath(path));

    private const string Manifest = """
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Applications>
            <Application Id="Helper" Executable="tools\helper.exe" />
            <Application Id="Spotify" Executable="Spotify.exe" />
          </Applications>
        </Package>
        """;

    [Fact]
    public void AppId_MatchesTheExecutable_IgnoringCase() => Assert.Equal("Spotify", PackagedApps.AppId(Manifest, "spotify.exe"));

    [Fact]
    public void AppId_NoMatch_FallsBackToTheFirstApplication() => Assert.Equal("Helper", PackagedApps.AppId(Manifest, "other.exe"));

    [Fact]
    public void AppId_BadOrEmptyManifest_IsNull()
    {
        Assert.Null(PackagedApps.AppId("<not xml", "a.exe"));
        Assert.Null(PackagedApps.AppId("<Package><Applications/></Package>", "a.exe"));
    }

    [Fact]
    public void Aumid_IsFamilyBangAppId() =>
        Assert.Equal(@"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", PackagedApps.LaunchPath("SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "Spotify"));

    [Fact]
    public void Resolve_ReadsTheManifestNextToThePackageRoot()
    {
        string? read = null;
        var launch = PackagedApps.Resolve(@"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.309.2055.0_x64__zpdnekdrzrea0\Spotify.exe",
            path => { read = path; return Manifest; });
        Assert.Equal(@"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.309.2055.0_x64__zpdnekdrzrea0\AppxManifest.xml", read);
        Assert.Equal(@"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", launch);
        Assert.Null(PackagedApps.Resolve(@"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.309.2055.0_x64__zpdnekdrzrea0\Spotify.exe", _ => null));
        Assert.Null(PackagedApps.Resolve(@"C:\Windows\explorer.exe", _ => Manifest));
    }

    // Spotify's real manifest: its main entry runs SpotifyMigrator.exe, so no entry matches Spotify.exe; the entry
    // named like the exe must win over whichever happens to be listed first.
    [Fact]
    public void AppId_NoExeMatch_PrefersTheEntryNamedLikeTheExe()
    {
        const string spotify = """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Applications>
              <Application Id="Widget" Executable="SpotifyXboxGamebarWebView.exe" />
              <Application Id="Spotify" Executable="SpotifyMigrator.exe" />
            </Applications></Package>
            """;
        Assert.Equal("Spotify", PackagedApps.AppId(spotify, "Spotify.exe"));
    }

    // Live, 2026-11-21: Spotify updated itself (1.309 → 1.310); the pinned exe's folder no longer exists. The app is
    // found again through its current install folder (asked for by family name).
    [Fact]
    public void Resolve_PackageUpdated_UsesTheCurrentInstall()
    {
        const string oldExe = @"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.309.2055.0_x64__zpdnekdrzrea0\Spotify.exe";
        const string newRoot = @"C:\Program Files\WindowsApps\SpotifyAB.SpotifyMusic_1.310.2586.0_x64__zpdnekdrzrea0";
        string? asked = null;
        var launch = PackagedApps.Resolve(oldExe,
            path => path == newRoot + @"\AppxManifest.xml" ? Manifest : null, // only the new folder exists
            family => { asked = family; return newRoot; });
        Assert.Equal("SpotifyAB.SpotifyMusic_zpdnekdrzrea0", asked);
        Assert.Equal(@"shell:AppsFolder\SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", launch);
    }
}
