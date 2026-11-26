using System.Windows;
using System.Windows.Media;

namespace NotWin.Theme;

/// <summary>
/// The dock's colours follow Windows' light / dark app theme (Plan 13 follow-up, user 2026-11-26: "everything should
/// softly blend"). <c>DockWindow.xaml</c> uses these keys as DynamicResources; <see cref="Apply"/> swaps frozen brushes
/// in the app's resources, so the open dock recolours at once. Dark = a black pill; light = a white pill
/// with ink glyphs; both frosted (slightly see-through, Lucent). Red accents stay red.
/// </summary>
public static class ShellTheme
{
    public static IReadOnlyList<string> Keys { get; } =
    [
        "Dock.Bg", "Dock.Line", "Dock.Text", "Dock.Dim", "Dock.Hover", "Dock.Active", "Dock.RunDot", "Dock.Ink",
        "Shell.Glass", "Shell.Pill", "Shell.PillEdge", "Shell.Text", "Shell.Dim", "Shell.Soft", "Shell.Faint", "Shell.Line",
        "Shell.Tile", "Shell.Card", "Shell.Hover", "Shell.OnBg", "Shell.OnText", "Shell.OnDim", "Shell.Fill", "Shell.Ink",
    ];

    /// <summary>True after <see cref="Apply"/> with light (for code that picks colours when it builds).</summary>
    public static bool Light { get; private set; }

    /// <summary>The theme was applied (code that sets colours itself re-applies them).</summary>
    public static event Action? Changed;

    private static readonly Dictionary<string, (Color Dark, Color Light)> Colours = new()
    {
        // Notch pill, quick settings, window previews (user 2026-11-26). Dark values are the old fixed colours.
        ["Shell.Glass"] = (Color.FromRgb(0x14, 0x14, 0x14), Color.FromArgb(0xF2, 0xF7, 0xF7, 0xF7)),   // panel / preview card
        ["Shell.Pill"] = (Color.FromRgb(0x00, 0x00, 0x00), Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),    // the notch
        ["Shell.PillEdge"] = (Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0x00, 0x00, 0x00)),
        ["Shell.Text"] = (Color.FromRgb(0xE8, 0xE8, 0xE8), Color.FromRgb(0x10, 0x10, 0x10)),
        ["Shell.Dim"] = (Color.FromRgb(0x99, 0x99, 0x99), Color.FromRgb(0x6B, 0x6B, 0x6B)),
        ["Shell.Soft"] = (Color.FromRgb(0xCC, 0xCC, 0xCC), Color.FromRgb(0x33, 0x33, 0x33)),
        ["Shell.Faint"] = (Color.FromRgb(0x77, 0x77, 0x77), Color.FromRgb(0x9A, 0x9A, 0x9A)),
        ["Shell.Line"] = (Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0x00, 0x00, 0x00)),
        ["Shell.Tile"] = (Color.FromRgb(0x26, 0x26, 0x26), Color.FromRgb(0xE4, 0xE4, 0xE4)),           // off tile, track, chip
        ["Shell.Card"] = (Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF), Color.FromRgb(0xFF, 0xFF, 0xFF)),    // notification card
        ["Shell.Hover"] = (Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0A, 0x00, 0x00, 0x00)),
        ["Shell.OnBg"] = (Color.FromRgb(0xE8, 0xE8, 0xE8), Color.FromRgb(0x1A, 0x1A, 0x1A)),           // an "on" tile
        ["Shell.OnText"] = (Color.FromRgb(0x11, 0x11, 0x11), Color.FromRgb(0xF2, 0xF2, 0xF2)),
        ["Shell.OnDim"] = (Color.FromRgb(0x55, 0x55, 0x55), Color.FromRgb(0x99, 0x99, 0x99)),
        ["Shell.Fill"] = (Color.FromRgb(0xE8, 0xE8, 0xE8), Color.FromRgb(0x1A, 0x1A, 0x1A)),           // slider fill, dots
        ["Shell.Ink"] = (Color.FromRgb(0x00, 0x00, 0x00), Color.FromRgb(0xFF, 0xFF, 0xFF)),            // a glyph cut out of Fill
        ["Dock.Bg"] = (Color.FromArgb(0xE6, 0x00, 0x00, 0x00), Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), // frosted (Lucent)
        ["Dock.Line"] = (Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0x00, 0x00, 0x00)),
        ["Dock.Text"] = (Color.FromRgb(0xE8, 0xE8, 0xE8), Color.FromRgb(0x10, 0x10, 0x10)),
        ["Dock.Dim"] = (Color.FromRgb(0x99, 0x99, 0x99), Color.FromRgb(0x6B, 0x6B, 0x6B)),
        ["Dock.Hover"] = (Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0F, 0x00, 0x00, 0x00)),
        ["Dock.Active"] = (Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x1A, 0x00, 0x00, 0x00)),
        ["Dock.RunDot"] = (Color.FromRgb(0xBB, 0xBB, 0xBB), Color.FromRgb(0x55, 0x55, 0x55)),
        ["Dock.Ink"] = (Color.FromRgb(0x10, 0x10, 0x10), Color.FromRgb(0xFF, 0xFF, 0xFF)), // the battery bolt's fill = the pill
    };

    public static void Apply(ResourceDictionary resources, bool light)
    {
        Light = light;
        foreach (var key in Keys)
        {
            var (dark, lite) = Colours[key];
            var brush = new SolidColorBrush(light ? lite : dark);
            brush.Freeze();
            resources[key] = brush;
        }
        Changed?.Invoke();
    }
}

/// <summary>Binds a property to a theme resource and returns the element (for object initialisers in code).</summary>
public static class ThemedExtensions
{
    public static T Themed<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }
}
