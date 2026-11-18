using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace NothingShell.Native;

/// <summary>Shell icon extraction: IShellItemImageFactory for a file path or a shell:AppsFolder AUMID item.</summary>
public static unsafe class IconNative
{
    /// <summary>
    /// A square 32-bit top-down BGRA image (straight alpha as returned by the shell) of the item's icon,
    /// or null if the shell cannot resolve it. <paramref name="parsingName"/> is a path or "shell:AppsFolder\AUMID".
    /// </summary>
    public static (int Size, byte[] Bgra)? GetIcon(string parsingName, int size)
    {
        var iid = IShellItemImageFactory.IID_Guid;
        void* raw;
        try
        {
            fixed (char* name = parsingName)
                if (PInvoke.SHCreateItemFromParsingName(name, null, &iid, &raw).Failed || raw is null) return null;
        }
        catch { return null; }

        var factory = (IShellItemImageFactory*)raw;
        HBITMAP hbm = default;
        try
        {
            // SIIGBF_BIGGERSIZEOK (0x1) | SIIGBF_ICONONLY (0x4)
            factory->GetImage(new SIZE { cx = size, cy = size }, (SIIGBF)0x5, &hbm);
            if (hbm.IsNull) return null;

            DIBSECTION ds;
            if (PInvoke.GetObject(hbm, sizeof(DIBSECTION), &ds) == 0 || ds.dsBm.bmBits is null || ds.dsBm.bmBitsPixel != 32)
                return null;
            int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight;
            if (w <= 0 || h <= 0) return null;
            var stride = w * 4;
            var src = new ReadOnlySpan<byte>(ds.dsBm.bmBits, ds.dsBm.bmWidthBytes * h);
            var dst = new byte[stride * h];
            var bottomUp = ds.dsBmih.biHeight > 0;
            for (var y = 0; y < h; y++)
            {
                var from = (bottomUp ? h - 1 - y : y) * ds.dsBm.bmWidthBytes;
                src.Slice(from, stride).CopyTo(dst.AsSpan(y * stride, stride));
            }
            return (w == h ? w : Math.Max(w, h), w == h ? dst : Pad(dst, w, h));
        }
        catch { return null; }
        finally
        {
            if (!hbm.IsNull) PInvoke.DeleteObject(hbm);
            factory->Release();
        }
    }

    // Non-square results are rare; letterbox into a square so the consumer only handles one shape.
    private static byte[] Pad(byte[] src, int w, int h)
    {
        var n = Math.Max(w, h);
        var dst = new byte[n * n * 4];
        var ox = (n - w) / 2; var oy = (n - h) / 2;
        for (var y = 0; y < h; y++)
            Buffer.BlockCopy(src, y * w * 4, dst, ((y + oy) * n + ox) * 4, w * 4);
        return dst;
    }
}
