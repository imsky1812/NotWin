using System.Windows;
using NotWin.Core.Notch;

namespace NotWin.Notch.Dashboard;

/// <summary>
/// One dashboard tile. <see cref="Build"/> returns a fresh element for the given size (the dashboard rebuilds on page,
/// size and edit changes); <see cref="Opened"/> / <see cref="Closed"/> start and stop anything live (media progress,
/// timer ticks) so nothing runs while the notch is closed (spec §7).
/// </summary>
public interface ITileView : IDisposable
{
    TileKind Kind { get; }
    FrameworkElement Build(TileSize size);
    void Opened();
    void Closed();
}

/// <summary>Icon + label tile used until a kind has its own view (and for kinds without one).</summary>
public sealed class PlaceholderTile(TileKind kind) : ITileView
{
    public TileKind Kind => kind;

    public static string Title(TileKind k) => k switch
    {
        TileKind.KeepAwake => "Keep awake",
        TileKind.GrabText => "Grab text",
        _ => k.ToString(),
    };

    public static string IconName(TileKind k) => k switch
    {
        TileKind.Timer => "timer",
        TileKind.Media => "music",
        TileKind.Clipboard => "clip",
        TileKind.Notes => "notes",
        TileKind.Shelf => "shelf",
        TileKind.KeepAwake => "cup",
        TileKind.Weather => "cloud",
        TileKind.GrabText => "text",
        TileKind.Phone => "phone",
        TileKind.Color => "picker",
        TileKind.Snip => "region",
        TileKind.Draw => "draw",
        _ => "settings",
    };

    public FrameworkElement Build(TileSize size)
    {
        var stack = new System.Windows.Controls.StackPanel();
        var icon = TileChrome.Icon(IconName(kind), 20);
        icon.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(icon);
        var label = TileChrome.Label(Title(kind));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(label);
        return TileChrome.Tile(stack, center: true);
    }

    public void Opened() { }
    public void Closed() { }
    public void Dispose() { }
}
