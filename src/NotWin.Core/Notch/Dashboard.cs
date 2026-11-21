using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotWin.Core.Notch;

/// <summary>Tiles of the open notch (spec §6.4; Scan QR dropped by the user 2026-11-19).</summary>
[JsonConverter(typeof(TolerantEnumConverter<TileKind>))]
public enum TileKind { Timer, Media, Clipboard, Notes, Shelf, KeepAwake, Weather, GrabText, Phone, Color, Snip, Draw, Settings }

/// <summary>One = 1×1, Wide = 2×1, Big = 2×2 cells (84 DIP cells, 10 DIP gaps).</summary>
[JsonConverter(typeof(TolerantEnumConverter<TileSize>))]
public enum TileSize { One, Wide, Big }

public sealed record TileSpec(TileKind Kind, TileSize Size);

public static class TileRules
{
    private static readonly TileSize[] Square = [TileSize.Big, TileSize.Wide, TileSize.One];
    private static readonly TileSize[] Awake = [TileSize.Wide, TileSize.One];
    private static readonly TileSize[] WeatherSizes = [TileSize.Wide, TileSize.One, TileSize.Big];
    private static readonly TileSize[] Tool = [TileSize.One, TileSize.Wide];

    public static (int W, int H) Cells(TileSize size) => size switch
    {
        TileSize.Wide => (2, 1),
        TileSize.Big => (2, 2),
        _ => (1, 1),
    };

    /// <summary>Allowed sizes in cycling order; the first is the default (mockup <c>SIZES</c>).</summary>
    public static IReadOnlyList<TileSize> Sizes(TileKind kind) => kind switch
    {
        TileKind.Timer or TileKind.Media or TileKind.Clipboard or TileKind.Notes or TileKind.Shelf => Square,
        TileKind.KeepAwake => Awake,
        TileKind.Weather => WeatherSizes,
        _ => Tool,
    };

    public static bool IsKnown(TileKind kind) => Enum.IsDefined(kind);
}

/// <summary>
/// The dashboard's pages and the tiles removed in edit mode. Immutable: every edit returns a new layout.
/// Pages never pack deeper than <see cref="MaxRows"/> (the notch band is sized for 3 rows).
/// </summary>
public sealed record DashboardLayout(IReadOnlyList<IReadOnlyList<TileSpec>> Pages, IReadOnlyList<TileSpec> Removed)
{
    public const int MaxRows = 3;

    public static DashboardLayout Default { get; } = new(
        [
            [new(TileKind.Timer, TileSize.Big), new(TileKind.Media, TileSize.Big),
             new(TileKind.Clipboard, TileSize.Big), new(TileKind.Notes, TileSize.Big)],
            [new(TileKind.Shelf, TileSize.Big), new(TileKind.KeepAwake, TileSize.Wide), new(TileKind.Weather, TileSize.Wide),
             new(TileKind.GrabText, TileSize.One), new(TileKind.Phone, TileSize.One), new(TileKind.Color, TileSize.One),
             new(TileKind.Snip, TileSize.One), new(TileKind.Draw, TileSize.One), new(TileKind.Settings, TileSize.One)],
        ],
        []);

    public static bool Fits(IReadOnlyList<TileSpec> page) => GridPacker.Rows(page.Select(t => t.Size).ToList()) <= MaxRows;

    /// <summary>The next allowed size that still fits within <see cref="MaxRows"/>; unchanged if none does.</summary>
    public DashboardLayout CycleSize(int page, int index)
    {
        var tiles = Pages[page];
        var tile = tiles[index];
        var sizes = TileRules.Sizes(tile.Kind);
        var at = Math.Max(0, IndexOf(sizes, tile.Size));
        for (var step = 1; step < sizes.Count; step++)
        {
            var candidate = tiles.ToList();
            candidate[index] = tile with { Size = sizes[(at + step) % sizes.Count] };
            if (Fits(candidate)) return WithPage(page, candidate);
        }
        return this;
    }

    public DashboardLayout Remove(int page, int index)
    {
        var tiles = Pages[page].ToList();
        var tile = tiles[index];
        tiles.RemoveAt(index);
        return WithPage(page, tiles) with { Removed = [.. Removed, tile] };
    }

    /// <summary>
    /// Brings back the first removed tile that fits on <paramref name="page"/>: at its default size, or else the next
    /// allowed size that fits (final review #3: a 2x2 default must not make "＋ Add" a dead button). Unchanged if none fits.
    /// </summary>
    public DashboardLayout AddBack(int page)
    {
        for (var i = 0; i < Removed.Count; i++)
        {
            foreach (var size in TileRules.Sizes(Removed[i].Kind))
            {
                var tiles = Pages[page].Append(Removed[i] with { Size = size }).ToList();
                if (!Fits(tiles)) continue;
                var removed = Removed.ToList();
                removed.RemoveAt(i);
                return WithPage(page, tiles) with { Removed = removed };
            }
        }
        return this;
    }

    /// <summary>Edit-mode drag onto another tile: the two swap places (unchanged if the result would not fit).</summary>
    public DashboardLayout Swap(int page, int a, int b)
    {
        if (a == b) return this;
        var tiles = Pages[page].ToList();
        (tiles[a], tiles[b]) = (tiles[b], tiles[a]);
        return Fits(tiles) ? WithPage(page, tiles) : this;
    }

    /// <summary>
    /// Edit-mode drag onto blank space: the tile moves to the end of the page and takes the largest allowed size that
    /// still fits, so it grows into a half-empty page and shrinks into a nearly full one (user request 2026-11-19).
    /// </summary>
    public DashboardLayout MoveToSpace(int page, int index)
    {
        var tiles = Pages[page].ToList();
        var tile = tiles[index];
        tiles.RemoveAt(index);
        return Placed(tiles, tile) is { } placed ? WithPage(page, placed) : this;
    }

    /// <summary>Edit-mode drag onto another page's dot: appended there at the largest size that fits; unchanged if none.</summary>
    public DashboardLayout MoveToPage(int from, int index, int to)
    {
        if (from == to) return MoveToSpace(from, index);
        var source = Pages[from].ToList();
        var tile = source[index];
        source.RemoveAt(index);
        if (Placed(Pages[to].ToList(), tile) is not { } target) return this;
        return WithPage(from, source).WithPage(to, target);
    }

    // The tile appended at its largest allowed size that fits, or null when none does.
    private static List<TileSpec>? Placed(List<TileSpec> tiles, TileSpec tile)
    {
        foreach (var size in TileRules.Sizes(tile.Kind).OrderByDescending(Area))
        {
            var candidate = tiles.Append(tile with { Size = size }).ToList();
            if (Fits(candidate)) return candidate;
        }
        return null;
    }

    private static int Area(TileSize s)
    {
        var (w, h) = TileRules.Cells(s);
        return w * h;
    }

    /// <summary>True when <see cref="AddBack"/> would bring a tile back (the dashboard shows "＋ Add" only then).</summary>
    public bool CanAddBack(int page) => !ReferenceEquals(AddBack(page), this);

    /// <summary>
    /// Makes any loaded layout valid: unknown kinds and duplicates are dropped, disallowed sizes become the default,
    /// pages deeper than 3 rows lose tiles from the end into Removed, and kinds found nowhere are appended to Removed.
    /// </summary>
    public static DashboardLayout Sanitize(DashboardLayout? loaded)
    {
        if (loaded?.Pages is not { Count: > 0 }) return Default;
        var seen = new HashSet<TileKind>();
        var removed = new List<TileSpec>();
        var pages = new List<IReadOnlyList<TileSpec>>();
        foreach (var page in loaded.Pages.Take(2))
        {
            var tiles = new List<TileSpec>();
            foreach (var t in page ?? [])
            {
                if (t is null || !TileRules.IsKnown(t.Kind) || !seen.Add(t.Kind)) continue;
                tiles.Add(Allowed(t));
            }
            while (tiles.Count > 0 && !Fits(tiles))
            {
                removed.Add(tiles[^1]);
                tiles.RemoveAt(tiles.Count - 1);
            }
            pages.Add(tiles);
        }
        while (pages.Count < 2) pages.Add([]);
        if (pages.All(p => p.Count == 0)) return Default;

        var placed = pages.SelectMany(p => p).Select(t => t.Kind).ToHashSet();
        var kept = (loaded.Removed ?? []).Where(t => t is not null && TileRules.IsKnown(t.Kind) && !placed.Contains(t.Kind))
            .Select(Allowed).Concat(removed)
            .DistinctBy(t => t.Kind).ToList();
        var known = placed.Concat(kept.Select(t => t.Kind)).ToHashSet();
        kept.AddRange(Enum.GetValues<TileKind>().Where(k => !known.Contains(k)).Select(k => new TileSpec(k, TileRules.Sizes(k)[0])));
        return new DashboardLayout(pages, kept);
    }

    private static TileSpec Allowed(TileSpec t) =>
        TileRules.Sizes(t.Kind).Contains(t.Size) ? t : t with { Size = TileRules.Sizes(t.Kind)[0] };

    private DashboardLayout WithPage(int page, IReadOnlyList<TileSpec> tiles)
    {
        var pages = Pages.ToList();
        pages[page] = tiles;
        return this with { Pages = pages };
    }

    private static int IndexOf(IReadOnlyList<TileSize> sizes, TileSize s)
    {
        for (var i = 0; i < sizes.Count; i++) if (sizes[i] == s) return i;
        return -1;
    }
}

/// <summary>
/// Enums as camelCase names. An unknown name (a tile from a newer or older version, a hand edit) reads as -1 instead
/// of throwing, so one bad entry can't make SettingsStore discard the whole settings file; Sanitize drops it.
/// </summary>
public sealed class TolerantEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var n)) return (T)(object)n;
        if (reader.TokenType == JsonTokenType.String && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var v)
            && Enum.IsDefined(v))
            return v;
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
        return (T)(object)(-1);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}
