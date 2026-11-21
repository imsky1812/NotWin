using System.Runtime.InteropServices;
using NotWin.Core;

namespace NotWin.Native;

/// <summary>Grabs the primary monitor into a GDI bitmap (BitBlt, SRCCOPY | CAPTUREBLT) for the notch's region tools.</summary>
public static class ScreenCaptureNative
{
    private const uint SrcCopy = 0x00CC0020, CaptureBlt = 0x40000000;

    /// <summary>The primary monitor's pixels as an HBITMAP (the caller frees it with <see cref="Delete"/>) and its bounds.</summary>
    public static (nint HBitmap, RectI Bounds) CapturePrimary()
    {
        var (bounds, _) = DockWindowNative.GetPrimaryMonitor();
        var w = bounds.Right - bounds.Left;
        var h = bounds.Bottom - bounds.Top;
        var screen = GetDC(0);
        if (screen == 0) throw new InvalidOperationException("GetDC failed");
        var mem = CreateCompatibleDC(screen);
        var bmp = CreateCompatibleBitmap(screen, w, h);
        try
        {
            var old = SelectObject(mem, bmp);
            var ok = BitBlt(mem, 0, 0, w, h, screen, bounds.Left, bounds.Top, SrcCopy | CaptureBlt);
            SelectObject(mem, old);
            if (!ok) { DeleteObject(bmp); throw new InvalidOperationException("BitBlt failed"); }
            return (bmp, bounds);
        }
        finally
        {
            DeleteDC(mem);
            ReleaseDC(0, screen);
        }
    }

    public static void Delete(nint hBitmap)
    {
        if (hBitmap != 0) DeleteObject(hBitmap);
    }

    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
}
