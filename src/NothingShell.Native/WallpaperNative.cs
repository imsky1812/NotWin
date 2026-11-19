using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace NothingShell.Native;

/// <summary>
/// The Windows desktop wallpaper (spec §6.7: chosen in Settings). Only <see cref="Apply"/> changes anything, and only
/// when the user picks a wallpaper.
/// </summary>
public static class WallpaperNative
{
    private const uint SpiGetDeskWallpaper = 0x0073, SpiSetDeskWallpaper = 0x0014, SpifUpdateIniFile = 0x01, SpifSendChange = 0x02;

    /// <summary>The current wallpaper file, or null (solid colour / slideshow / unreadable).</summary>
    public static string? Current()
    {
        var sb = new StringBuilder(1024);
        if (!SystemParametersInfoW(SpiGetDeskWallpaper, (uint)sb.Capacity, sb, 0)) return null;
        var path = sb.ToString();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    /// <summary>Sets <paramref name="path"/> as the wallpaper, stretched to fill (WallpaperStyle 10, no tiling).</summary>
    public static bool Apply(string path)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
            {
                key?.SetValue("WallpaperStyle", "10");
                key?.SetValue("TileWallpaper", "0");
            }
            return SystemParametersInfoW(SpiSetDeskWallpaper, 0, new StringBuilder(path), SpifUpdateIniFile | SpifSendChange);
        }
        catch (Exception) { return false; }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint action, uint param, StringBuilder value, uint winIni);
}
