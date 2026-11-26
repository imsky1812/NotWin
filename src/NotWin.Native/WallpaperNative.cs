using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace NotWin.Native;

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

    /// <summary>
    /// The file the wallpaper was set from: Active Desktop's <c>WallpaperSource</c> when present (after
    /// <see cref="ApplyFaded"/> Windows may report its cached TranscodedWallpaper copy instead), else <see cref="Current"/>.
    /// </summary>
    public static string? CurrentSource()
    {
        var current = Current();
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Internet Explorer\Desktop\General");
            if (k?.GetValue("WallpaperSource") is string source && !string.IsNullOrWhiteSpace(source)
                && (current is null || Path.GetFileName(current).StartsWith("TranscodedWallpaper", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileName(current), Path.GetFileName(source), StringComparison.OrdinalIgnoreCase)))
                return source;
        }
        catch (Exception) { }
        return current;
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

    /// <summary>
    /// Sets the wallpaper with Explorer's cross-fade (Plan 13 follow-up, user 2026-11-26: "make the wallpaper change
    /// smoother"): the IActiveDesktop path, after asking Progman to make its fade layer (message 0x052C). Runs on its own
    /// STA thread (the COM object needs one); falls back to <see cref="Apply"/> (an instant change) when anything fails.
    /// </summary>
    public static bool ApplyFaded(string path)
    {
        var ok = false;
        var t = new Thread(() =>
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
                {
                    key?.SetValue("WallpaperStyle", "10");
                    key?.SetValue("TileWallpaper", "0");
                }
                var progman = FindWindowW("Progman", null);
                if (progman != 0) SendMessageTimeoutW(progman, 0x052C, 0, 0, 0, 500, out _);
                var desktop = (IActiveDesktop)new ActiveDesktopClass();
                try
                {
                    ok = desktop.SetWallpaper(path, 0) == 0
                         && desktop.ApplyChanges(AdApplyAll | AdApplyForce | AdApplyBufferedRefresh) == 0;
                }
                finally { Marshal.ReleaseComObject(desktop); }
            }
            catch (Exception) { ok = false; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        if (!t.Join(TimeSpan.FromSeconds(10))) return false;
        return ok || Apply(path);
    }

    private const int AdApplyAll = 0x7, AdApplyForce = 0x8, AdApplyBufferedRefresh = 0x10;

    [ComImport, Guid("75048700-EF1F-11D0-9888-006097DEACF9")]
    private class ActiveDesktopClass;

    // Only the first three methods are called; the vtable order up to them must match shlobj's IActiveDesktop.
    [ComImport, Guid("F490EB00-1240-11D1-9888-006097DEACF9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActiveDesktop
    {
        [PreserveSig] int ApplyChanges(int flags);

        [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] StringBuilder wallpaper, int length, int flags);

        [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string wallpaper, int reserved);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(nint hWnd, int msg, nint wParam, nint lParam, int flags, int timeout, out nint result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint action, uint param, StringBuilder value, uint winIni);
}
