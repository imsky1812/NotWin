using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NotWin.Native;

namespace NotWin.Dock;

/// <summary>
/// Full-colour app icons, frozen and cached by key. Bounded LRU so memory cannot
/// grow with the number of distinct apps seen. UI thread only.
/// </summary>
public sealed class AppIconCache
{
    private const int Capacity = 64;

    /// <summary>At most this many "no icon" keys are remembered (oldest dropped first).</summary>
    public const int MaxNegative = 256;
    private static readonly long NegativeTtlMs = (long)TimeSpan.FromMinutes(5).TotalMilliseconds;

    private readonly Dictionary<string, LinkedListNode<(string Key, ImageSource Image)>> _map = [];
    private readonly LinkedList<(string Key, ImageSource Image)> _lru = [];
    private readonly Dictionary<int, ImageSource> _placeholders = [];
    // Keys whose icon failed to load, with the time of the failure: not retried for 5 minutes, so a broken
    // shortcut does not cost a shell call on every dock refresh.
    private readonly Dictionary<string, long> _negative = [];
    private readonly Queue<string> _negativeOrder = new();
    private readonly Func<string, int, ImageSource?> _load;
    private readonly Func<long> _nowMs;

    public AppIconCache() : this(Load, () => Environment.TickCount64) { }

    /// <summary>For tests: inject the loader and the millisecond clock.</summary>
    public AppIconCache(Func<string, int, ImageSource?> load, Func<long> nowMs)
    {
        _load = load;
        _nowMs = nowMs;
    }

    public int Count => _map.Count;

    public int NegativeCount => _negative.Count;

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

        var now = _nowMs();
        if (_negative.TryGetValue(key, out var failedAt) && now - failedAt <= NegativeTtlMs) return Placeholder(sizePx);

        var image = _load(exePathOrAumid, sizePx)
                    ?? (fallbackPath is { Length: > 0 } ? _load(fallbackPath, sizePx) : null);
        if (image is null)
        {
            RememberFailure(key, now);
            return Placeholder(sizePx);
        }
        _negative.Remove(key);
        _map[key] = _lru.AddFirst((key, image));
        while (_map.Count > Capacity)
        {
            var last = _lru.Last!;
            _lru.RemoveLast();
            _map.Remove(last.Value.Key);
        }
        return image;
    }

    private void RememberFailure(string key, long now)
    {
        if (!_negative.ContainsKey(key)) _negativeOrder.Enqueue(key);
        _negative[key] = now;
        while (_negative.Count > MaxNegative && _negativeOrder.TryDequeue(out var oldest)) _negative.Remove(oldest);
    }

    private static ImageSource? Load(string exePathOrAumid, int sizePx)
    {
        // A Store app's exe resolves to its app id even when its version folder is gone (the app updated itself).
        var parsing = ParsingName(exePathOrAumid, StoreApps.ReadText, StoreApps.CurrentRoot);
        if (Path.IsPathRooted(parsing) && !File.Exists(parsing)) return null;
        var icon = IconNative.GetIcon(parsing, sizePx);
        return icon is { } i ? ToBitmap(i.Bgra, i.Size) : null;
    }

    /// <summary>
    /// What to ask the shell for an icon: an app id becomes <c>shell:AppsFolder\&lt;id&gt;</c>; a Store app's exe inside
    /// WindowsApps becomes its app id too (user report 2026-11-20: WhatsApp's exe has no real icon of its own, its
    /// package does); any other path is used as it is.
    /// </summary>
    public static string ParsingName(string exePathOrAumid, Func<string, string?> readText, Func<string, string?>? currentRoot = null)
    {
        if (!Path.IsPathRooted(exePathOrAumid)) return @"shell:AppsFolder\" + exePathOrAumid;
        return NotWin.Core.PackagedApps.Resolve(exePathOrAumid, readText, currentRoot) ?? exePathOrAumid;
    }

    private static BitmapSource ToBitmap(byte[] bgra, int size)
    {
        // Original colours. IShellItemImageFactory returns STRAIGHT alpha for these icons (measured: soft-edge
        // pixels routinely have colour > alpha, impossible if premultiplied), hence Bgra32.
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, bgra, size * 4);
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
