using System.IO;
using Windows.Management.Deployment;

namespace NothingShell.Dock;

/// <summary>
/// Where a Store app (package family) is installed now — Store apps update themselves into a new version folder, which
/// leaves pins and window paths pointing at a folder that no longer exists (live, 2026-11-21: Spotify 1.309 → 1.310).
/// </summary>
public static class StoreApps
{
    private static readonly Lazy<PackageManager?> Manager = new(() =>
    {
        try { return new PackageManager(); }
        catch (Exception e) { Log.Error("package manager unavailable", e); return null; }
    });

    /// <summary>The install folder of the current user's package with <paramref name="family"/>, or null.</summary>
    public static string? CurrentRoot(string family)
    {
        try { return Manager.Value?.FindPackagesForUser(string.Empty, family).FirstOrDefault()?.InstalledLocation?.Path; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Reads a package manifest (null when it cannot be read).</summary>
    public static string? ReadText(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
