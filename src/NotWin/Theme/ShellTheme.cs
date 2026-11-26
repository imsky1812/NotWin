using System.Windows;
using System.Windows.Media;

namespace NotWin.Theme;

/// <summary>
/// The dock's colours follow Windows' light / dark app theme (Plan 13 follow-up, user 2026-11-26: "everything should
/// softly blend"). <c>DockWindow.xaml</c> uses these keys as DynamicResources; <see cref="Apply"/> swaps frozen brushes
/// in the app's resources, so the open dock recolours at once. Dark = today's black pill; light = a soft #EDEDED pill
/// with ink glyphs. Red accents stay red.
/// </summary>
public static class ShellTheme
{
    public static IReadOnlyList<string> Keys { get; } =
        ["Dock.Bg", "Dock.Line", "Dock.Text", "Dock.Dim", "Dock.Hover", "Dock.Active", "Dock.RunDot", "Dock.Ink"];

    private static readonly Dictionary<string, (Color Dark, Color Light)> Colours = new()
    {
        ["Dock.Bg"] = (Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0xED, 0xED, 0xED)),
        ["Dock.Line"] = (Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0x00, 0x00, 0x00)),
        ["Dock.Text"] = (Color.FromRgb(0xE8, 0xE8, 0xE8), Color.FromRgb(0x10, 0x10, 0x10)),
        ["Dock.Dim"] = (Color.FromRgb(0x99, 0x99, 0x99), Color.FromRgb(0x6B, 0x6B, 0x6B)),
        ["Dock.Hover"] = (Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0F, 0x00, 0x00, 0x00)),
        ["Dock.Active"] = (Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x1A, 0x00, 0x00, 0x00)),
        ["Dock.RunDot"] = (Color.FromRgb(0xBB, 0xBB, 0xBB), Color.FromRgb(0x55, 0x55, 0x55)),
        ["Dock.Ink"] = (Color.FromRgb(0x10, 0x10, 0x10), Color.FromRgb(0xED, 0xED, 0xED)), // the battery bolt's fill = the pill
    };

    public static void Apply(ResourceDictionary resources, bool light)
    {
        foreach (var key in Keys)
        {
            var (dark, lite) = Colours[key];
            var brush = new SolidColorBrush(light ? lite : dark);
            brush.Freeze();
            resources[key] = brush;
        }
    }
}
