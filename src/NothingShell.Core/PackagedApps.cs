using System.Xml.Linq;

namespace NothingShell.Core;

/// <summary>
/// Store (packaged) apps whose window gave the dock only an exe inside <c>Program Files\WindowsApps</c> (user report
/// 2026-11-20: Spotify and Claude pinned that way would not start — Windows refuses to run a package's exe directly).
/// They have to be started by their app id: <c>shell:AppsFolder\&lt;family&gt;!&lt;application id&gt;</c>, where the family
/// comes from the package folder name and the application id from the package's AppxManifest.xml.
/// </summary>
public static class PackagedApps
{
    private const string Marker = @"\WindowsApps\";

    /// <summary>
    /// For an exe inside a package folder (<c>…\WindowsApps\Name_Version_Arch__PublisherId\…</c>): the package root,
    /// its family name (<c>Name_PublisherId</c>) and the exe's path inside the package; else null.
    /// </summary>
    public static (string Root, string Family, string RelativeExe)? FromExePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var at = path.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var rest = path[(at + Marker.Length)..];
        var slash = rest.IndexOf('\\');
        if (slash <= 0 || slash == rest.Length - 1) return null;
        var folder = rest[..slash];
        var pub = folder.LastIndexOf("__", StringComparison.Ordinal);
        var firstUnderscore = folder.IndexOf('_');
        if (pub <= 0 || firstUnderscore <= 0 || pub + 2 >= folder.Length) return null;
        var family = folder[..firstUnderscore] + "_" + folder[(pub + 2)..];
        return (path[..(at + Marker.Length + slash)], family, rest[(slash + 1)..]);
    }

    /// <summary>
    /// The manifest's application whose Executable is <paramref name="relativeExe"/>, else the one whose Id is the exe's
    /// name, else the first one; null if none.
    /// </summary>
    public static string? AppId(string manifestXml, string relativeExe)
    {
        try
        {
            var apps = XDocument.Parse(manifestXml).Descendants().Where(e => e.Name.LocalName == "Application").ToList();
            var match = apps.FirstOrDefault(a =>
                string.Equals(((string?)a.Attribute("Executable"))?.Replace('/', '\\'), relativeExe, StringComparison.OrdinalIgnoreCase));
            // No exact match (Spotify's main entry runs SpotifyMigrator.exe): the entry named like the exe, else the first.
            var stem = Path.GetFileNameWithoutExtension(relativeExe);
            match ??= apps.FirstOrDefault(a => string.Equals((string?)a.Attribute("Id"), stem, StringComparison.OrdinalIgnoreCase));
            return (string?)(match ?? apps.FirstOrDefault())?.Attribute("Id");
        }
        catch (System.Xml.XmlException) { return null; }
    }

    public static string LaunchPath(string family, string appId) => @"shell:AppsFolder\" + family + "!" + appId;

    /// <summary>
    /// The <c>shell:AppsFolder</c> launch path for a packaged app's exe, reading its manifest through
    /// <paramref name="readText"/> (null when unreadable); null when the path is not inside a package.
    /// </summary>
    /// <param name="currentRoot">Where the package with this family name is installed now (null if unknown): used when
    /// the app has updated itself and the exe's old version folder is gone (live, 2026-11-21: Spotify 1.309 → 1.310).</param>
    public static string? Resolve(string? exePath, Func<string, string?> readText, Func<string, string?>? currentRoot = null)
    {
        if (FromExePath(exePath) is not { } p) return null;
        var xml = readText(Path.Combine(p.Root, "AppxManifest.xml"));
        if (xml is null && currentRoot?.Invoke(p.Family) is { } now && !string.Equals(now, p.Root, StringComparison.OrdinalIgnoreCase))
            xml = readText(Path.Combine(now, "AppxManifest.xml"));
        return xml is not null && AppId(xml, p.RelativeExe) is { } id ? LaunchPath(p.Family, id) : null;
    }
}
