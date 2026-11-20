using System.Runtime.InteropServices;
using NothingShell.Core;

namespace NothingShell.Native;

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

    /// <summary>
    /// A screen region as grey values, one per <paramref name="stepPx"/> cell (sampled at the cell centre, row-major,
    /// (width / step) × (height / step)) — the dot drain's picture (Plan 7). Never kept; null on failure.
    /// </summary>
    public static unsafe byte[]? CaptureGray(RectI screen, int stepPx)
    {
        var w = screen.Right - screen.Left;
        var h = screen.Bottom - screen.Top;
        if (w <= 0 || h <= 0 || stepPx <= 0) return null;
        int cols = w / stepPx, rows = h / stepPx;
        if (cols == 0 || rows == 0) return [];
        var dc = GetDC(0);
        if (dc == 0) return null;
        var mem = CreateCompatibleDC(dc);
        var info = new BitmapInfo { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
        var dib = CreateDIBSection(dc, ref info, 0, out var bits, 0, 0);
        try
        {
            if (dib == 0 || bits == 0) return null;
            var old = SelectObject(mem, dib);
            var ok = BitBlt(mem, 0, 0, w, h, dc, screen.Left, screen.Top, SrcCopy | CaptureBlt);
            SelectObject(mem, old);
            if (!ok) return null;
            var px = (byte*)bits;
            var gray = new byte[cols * rows];
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    var x = c * stepPx + stepPx / 2;
                    var y = r * stepPx + stepPx / 2;
                    var p = px + ((long)y * w + x) * 4; // BGRA
                    gray[r * cols + c] = (byte)((p[2] * 299 + p[1] * 587 + p[0] * 114) / 1000);
                }
            return gray;
        }
        finally
        {
            if (dib != 0) DeleteObject(dib);
            DeleteDC(mem);
            ReleaseDC(0, dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
        public int Colors; // one RGBQUAD slot (unused for 32 bpp)
    }

    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);

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
