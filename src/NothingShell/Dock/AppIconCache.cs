using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NothingShell.Native;

namespace NothingShell.Dock;

/// <summary>
/// Greyscale (luminance only, alpha kept) app icons, frozen and cached by key. Bounded LRU so memory cannot
/// grow with the number of distinct apps seen. UI thread only.
/// </summary>
public sealed class AppIconCache
{
    private const int Capacity = 64;

    private readonly Dictionary<string, LinkedListNode<(string Key, ImageSource Image)>> _map = [];
    private readonly LinkedList<(string Key, ImageSource Image)> _lru = [];
    private readonly Dictionary<int, ImageSource> _placeholders = [];

    public int Count => _map.Count;

    /// <summary>
    /// Icon for an exe/file path or a packaged-app AUMID (resolved through shell:AppsFolder), at
    /// <paramref name="sizePx"/> device pixels. Never null: a neutral placeholder is returned when the shell has no icon.
    /// </summary>
    public ImageSource Get(string exePathOrAumid, int sizePx) => Get(exePathOrAumid, null, sizePx);

    /// <summary>As <see cref="Get(string,int)"/>, trying <paramref name="fallbackPath"/> if the first key has no icon.</summary>
    public ImageSource Get(string exePathOrAumid, string? fallbackPath, int sizePx)
    {
        var key = $"{sizePx}|{exePathOrAumid.ToLowerInvariant()}";
        if (_map.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Image;
        }

        var image = Load(exePathOrAumid, sizePx)
                    ?? (fallbackPath is { Length: > 0 } ? Load(fallbackPath, sizePx) : null)
                    ?? Placeholder(sizePx);
        _map[key] = _lru.AddFirst((key, image));
        while (_map.Count > Capacity)
        {
            var last = _lru.Last!;
            _lru.RemoveLast();
            _map.Remove(last.Value.Key);
        }
        return image;
    }

    private static ImageSource? Load(string exePathOrAumid, int sizePx)
    {
        var rooted = Path.IsPathRooted(exePathOrAumid);
        if (rooted && !File.Exists(exePathOrAumid)) return null;
        var parsing = rooted ? exePathOrAumid : @"shell:AppsFolder\" + exePathOrAumid;
        var icon = IconNative.GetIcon(parsing, sizePx);
        return icon is { } i ? Greyscale(i.Bgra, i.Size) : null;
    }

    private static BitmapSource Greyscale(byte[] bgra, int size)
    {
        var px = (byte[])bgra.Clone();
        for (var o = 0; o + 3 < px.Length; o += 4)
        {
            // Rec. 709 luminance on the straight (non-premultiplied) colour; alpha is untouched.
            var lum = (byte)(0.0722 * px[o] + 0.7152 * px[o + 1] + 0.2126 * px[o + 2] + 0.5);
            px[o] = px[o + 1] = px[o + 2] = lum;
        }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, px, size * 4);
        bmp.Freeze();
        return bmp;
    }

    private ImageSource Placeholder(int sizePx)
    {
        if (_placeholders.TryGetValue(sizePx, out var p)) return p;
        // A soft grey rounded square.
        var px = new byte[sizePx * sizePx * 4];
        double inset = sizePx / 5.0, radius = sizePx / 5.0, hi = sizePx - 1 - inset;
        for (var y = 0; y < sizePx; y++)
            for (var x = 0; x < sizePx; x++)
            {
                var dx = Math.Max(Math.Max(inset + radius - x, x - (hi - radius)), 0);
                var dy = Math.Max(Math.Max(inset + radius - y, y - (hi - radius)), 0);
                if (x < inset || x > hi || y < inset || y > hi || dx * dx + dy * dy > radius * radius) continue;
                var o = (y * sizePx + x) * 4;
                px[o] = px[o + 1] = px[o + 2] = 0x80;
                px[o + 3] = 0xFF;
            }
        var bmp = BitmapSource.Create(sizePx, sizePx, 96, 96, PixelFormats.Bgra32, null, px, sizePx * 4);
        bmp.Freeze();
        return _placeholders[sizePx] = bmp;
    }
}
