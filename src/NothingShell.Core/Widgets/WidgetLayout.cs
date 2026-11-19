using System.Text.Json.Serialization;
using NothingShell.Core.Notch;

namespace NothingShell.Core.Widgets;

/// <summary>Desktop widgets (spec §6.5).</summary>
[JsonConverter(typeof(TolerantEnumConverter<WidgetKind>))]
public enum WidgetKind { Clock, Snake, Timer, Calendar, Weather, Media, Battery, Photo, Folder }

/// <summary>A widget's size in grid cells (136 px cells with 12 px gaps, unscaled; spec §5).</summary>
public readonly record struct WidgetSize(int W, int H)
{
    public static int Px(int cells) => cells * 136 + (cells - 1) * 12;
    [JsonIgnore] public int PxW => Px(W);
    [JsonIgnore] public int PxH => Px(H);
}

/// <summary>
/// A widget's own settings (Plan 5b): a photo frame's shape ("square" / "circle") and photo file names in its folder;
/// an app folder's dot picture and app ids. Null fields = defaults.
/// </summary>
public sealed record WidgetConfig(string? Shape = null, IReadOnlyList<string>? Photos = null, string? Picture = null, IReadOnlyList<string>? Apps = null);

/// <summary>
/// A widget instance at (X, Y): unscaled pixels from the layout origin, on the 74 px snap grid. <see cref="Id"/> tells
/// instances of the same kind apart (several photo frames and folders); "" until <see cref="DesktopLayout.Sanitize"/>
/// gives it one.
/// </summary>
public sealed record WidgetPlacement(WidgetKind Kind, WidgetSize Size, int X, int Y, string Id = "", WidgetConfig? Config = null);

public static class WidgetRules
{
    private static readonly WidgetSize S11 = new(1, 1), S21 = new(2, 1), S22 = new(2, 2), S31 = new(3, 1), S33 = new(3, 3), S42 = new(4, 2);

    /// <summary>Allowed sizes in cycling order; the first is the default (mockup <c>W[].sizes</c>).</summary>
    public static IReadOnlyList<WidgetSize> Sizes(WidgetKind kind) => kind switch
    {
        WidgetKind.Clock => [S31, S21, S42],
        WidgetKind.Snake => [S22, S33],
        WidgetKind.Timer => [S22, S11, S21],
        WidgetKind.Calendar => [S22, S21, S11],
        WidgetKind.Weather => [S21, S11, S22],
        WidgetKind.Media => [S21, S11, S22],
        WidgetKind.Photo => [S22, S11, S33],
        WidgetKind.Folder => [S11, S22],
        _ => [S11, S21, S22],
    };

    /// <summary>Photo frames and app folders may be added any number of times; every other kind at most once.</summary>
    public static bool IsRepeatable(WidgetKind kind) => kind is WidgetKind.Photo or WidgetKind.Folder;

    /// <summary>The order of the edit mode's ＋ Add picker.</summary>
    public static IReadOnlyList<WidgetKind> PickerOrder { get; } =
    [
        WidgetKind.Photo, WidgetKind.Folder, WidgetKind.Clock, WidgetKind.Snake, WidgetKind.Timer, WidgetKind.Calendar,
        WidgetKind.Weather, WidgetKind.Media, WidgetKind.Battery,
    ];
}

/// <summary>
/// Where the desktop widgets are (spec §6.5 edit mode, mockup <c>W</c> / <c>overlaps</c> / drag-to-place): positions
/// snap to a 74 px grid (half a cell); a move that would overlap another widget is refused; a resize may overlap and is
/// then flagged until the widget is moved. Immutable: every change returns a new layout.
/// </summary>
public sealed record DesktopLayout(IReadOnlyList<WidgetPlacement> Widgets)
{
    public const int Snap = 74, Gap = 6;

    public static DesktopLayout Default { get; } = new(
    [
        new(WidgetKind.Clock, new(3, 1), 0, 0),
        new(WidgetKind.Snake, new(2, 2), 0, 148),
        new(WidgetKind.Timer, new(2, 2), 296, 148),
        new(WidgetKind.Calendar, new(2, 2), 592, 148),
        new(WidgetKind.Weather, new(2, 1), 888, 148),
        new(WidgetKind.Media, new(2, 1), 888, 296),
        new(WidgetKind.Battery, new(1, 1), 0, 444),
    ]);

    public static int SnapTo(double v) => (int)Math.Round(v / Snap, MidpointRounding.AwayFromZero) * Snap;

    /// <summary>True when widget <paramref name="index"/> overlaps another (the mockup's rule, 6 px apart).</summary>
    public bool Overlaps(int index) => OverlapsAt(Widgets, index, Widgets[index].Size, Widgets[index].X, Widgets[index].Y);

    private static bool OverlapsAt(IReadOnlyList<WidgetPlacement> all, int index, WidgetSize size, int x, int y)
    {
        for (var i = 0; i < all.Count; i++)
        {
            if (i == index) continue;
            var p = all[i];
            if (x < p.X + p.Size.PxW + Gap && x + size.PxW + Gap > p.X && y < p.Y + p.Size.PxH + Gap && y + size.PxH + Gap > p.Y)
                return true;
        }
        return false;
    }

    /// <summary>Snaps and clamps (x, y) into the area; refused (unchanged, false) when the result would overlap.</summary>
    public (DesktopLayout Layout, bool Moved) TryMove(int index, double x, double y, int areaW, int areaH)
    {
        var p = Widgets[index];
        var (nx, ny) = Clamp(p.Size, SnapTo(x), SnapTo(y), areaW, areaH);
        if (OverlapsAt(Widgets, index, p.Size, nx, ny)) return (this, false);
        return (With(index, p with { X = nx, Y = ny }), true);
    }

    /// <summary>The next allowed size; may overlap (then <see cref="Overlaps"/> flags it until moved).</summary>
    public DesktopLayout CycleSize(int index)
    {
        var p = Widgets[index];
        var sizes = WidgetRules.Sizes(p.Kind);
        var at = Math.Max(0, IndexOf(sizes, p.Size));
        return With(index, p with { Size = sizes[(at + 1) % sizes.Count] });
    }

    /// <summary>
    /// Makes a loaded layout valid for an area: null = the default seven (first run); an empty layout stays empty;
    /// unknown kinds and second copies of one-off kinds dropped (the first wins); disallowed sizes back to the default;
    /// positions snapped and clamped (overlaps kept, flagged); entries without an id, or with a repeated one, get a new
    /// id. Removed widgets are never added back (Plan 5b).
    /// </summary>
    public static DesktopLayout Sanitize(DesktopLayout? loaded, int areaW, int areaH, Func<string>? newId = null)
    {
        newId ??= () => NewId(Random.Shared);
        var source = loaded?.Widgets ?? Default.Widgets;
        var seenKinds = new HashSet<WidgetKind>();
        var seenIds = new HashSet<string>();
        var list = new List<WidgetPlacement>();
        foreach (var p in source)
        {
            if (p is null || !Enum.IsDefined(p.Kind)) continue;
            if (!WidgetRules.IsRepeatable(p.Kind) && !seenKinds.Add(p.Kind)) continue;
            var size = WidgetRules.Sizes(p.Kind).Contains(p.Size) ? p.Size : WidgetRules.Sizes(p.Kind)[0];
            var (x, y) = Clamp(size, SnapTo(p.X), SnapTo(p.Y), areaW, areaH);
            var id = p.Id;
            while (string.IsNullOrEmpty(id) || !seenIds.Add(id)) id = newId();
            list.Add(p with { Size = size, X = x, Y = y, Id = id });
        }
        return new DesktopLayout(list);
    }

    /// <summary>A new instance id: 8 lowercase hex characters.</summary>
    public static string NewId(Random rng) => rng.Next().ToString("x8", System.Globalization.CultureInfo.InvariantCulture)[..8];

    /// <summary>The index of the widget with <paramref name="id"/>, or -1.</summary>
    public int IndexOf(string id)
    {
        for (var i = 0; i < Widgets.Count; i++) if (Widgets[i].Id == id) return i;
        return -1;
    }

    /// <summary>What ＋ Add offers: repeatable kinds always, a one-off kind only while it is not on the desktop.</summary>
    public IReadOnlyList<WidgetKind> Addable() =>
        WidgetRules.PickerOrder.Where(k => WidgetRules.IsRepeatable(k) || Widgets.All(p => p.Kind != k)).ToList();

    /// <summary>
    /// A new widget at its default size in the first free snapped spot (from the top-left); with no room, at the
    /// top-left, where it is flagged as overlapping until moved.
    /// </summary>
    public (DesktopLayout Layout, string Id) Add(WidgetKind kind, int areaW, int areaH, Func<string> newId, WidgetConfig? config = null)
    {
        var size = WidgetRules.Sizes(kind)[0];
        var (x, y) = FirstFree(Widgets, size, areaW, areaH) ?? (0, 0);
        var id = newId();
        while (IndexOf(id) >= 0) id = newId();
        return (new DesktopLayout([.. Widgets, new WidgetPlacement(kind, size, x, y, id, config)]), id);
    }

    /// <summary>Without the widget <paramref name="id"/> (unchanged when there is none).</summary>
    public DesktopLayout Remove(string id) =>
        IndexOf(id) < 0 ? this : new DesktopLayout(Widgets.Where(p => p.Id != id).ToList());

    /// <summary>The widget <paramref name="id"/> with new settings (unchanged when there is none).</summary>
    public DesktopLayout WithConfig(string id, WidgetConfig config)
    {
        var i = IndexOf(id);
        return i < 0 ? this : With(i, Widgets[i] with { Config = config });
    }

    private static (int X, int Y)? FirstFree(IReadOnlyList<WidgetPlacement> list, WidgetSize size, int areaW, int areaH)
    {
        for (var y = 0; y + size.PxH <= areaH; y += Snap)
            for (var x = 0; x + size.PxW <= areaW; x += Snap)
                if (!OverlapsAt(list, -1, size, x, y)) return (x, y);
        return null;
    }

    private static (int X, int Y) Clamp(WidgetSize size, int x, int y, int areaW, int areaH)
    {
        var maxX = Math.Max(0, (areaW - size.PxW) / Snap * Snap);
        var maxY = Math.Max(0, (areaH - size.PxH) / Snap * Snap);
        return (Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
    }

    private DesktopLayout With(int index, WidgetPlacement p)
    {
        var list = Widgets.ToList();
        list[index] = p;
        return new DesktopLayout(list);
    }

    private static int IndexOf(IReadOnlyList<WidgetSize> sizes, WidgetSize s)
    {
        for (var i = 0; i < sizes.Count; i++) if (sizes[i] == s) return i;
        return -1;
    }
}
