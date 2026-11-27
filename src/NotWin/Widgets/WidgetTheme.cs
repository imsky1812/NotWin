using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NotWin.Notch.Dashboard;

namespace NotWin.Widgets;

/// <summary>
/// The desktop widgets' colours follow Windows' app theme (Plan 13 follow-up): dark = #1A1A1A cards with light text,
/// light = #E8E8E8 cards with ink #101010 text and dots. Red stays red; the bare clock keeps sampling the wallpaper; edit
/// chrome and popups stay dark. The brushes are frozen and swapped on a change (widgets live on the UI thread, tests
/// on their own threads), and <see cref="Changed"/> makes <see cref="DesktopWidgets"/> rebuild every card once.
/// </summary>
public static class WidgetTheme
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    // Lucent (user, 2026-11-26): frosted cards — slightly see-through, so the soft blurred wallpaper shows through.
    private static readonly Brush DarkCard = TileChrome.FrozenArgb(0xE0, 0x22, 0x22, 0x22), LightCard = TileChrome.FrozenArgb(0xE6, 0xFF, 0xFF, 0xFF);
    private static readonly Brush DarkEdge = TileChrome.FrozenArgb(0x21, 0xFF, 0xFF, 0xFF), LightEdge = TileChrome.FrozenArgb(0x12, 0, 0, 0);
    private static readonly Brush DarkText = TileChrome.Text, LightText = TileChrome.Frozen(0x10, 0x10, 0x10);
    private static readonly Brush DarkDim = TileChrome.Dim, LightDim = TileChrome.Frozen(0x6B, 0x6B, 0x6B);
    private static readonly Brush DarkUnlit = TileChrome.FrozenArgb(0x40, 0xE8, 0xE8, 0xE8), LightUnlit = TileChrome.FrozenArgb(0x1F, 0x10, 0x10, 0x10);
    private static bool s_following;

    public static bool Light { get; private set; }

    public static Brush Card => Light ? LightCard : DarkCard;
    public static Brush Edge => Light ? LightEdge : DarkEdge;

    /// <summary>Main content: text, dot numerals, icons, hands.</summary>
    public static Brush Text => Light ? LightText : DarkText;

    /// <summary>Labels and secondary text.</summary>
    public static Brush Dim => Light ? LightDim : DarkDim;

    /// <summary>Unlit dots in rings, bars and charts; tracks and grid lines.</summary>
    public static Brush Unlit => Light ? LightUnlit : DarkUnlit;

    private static readonly Brush DarkSoft = TileChrome.Frozen(0xCC, 0xCC, 0xCC), LightSoft = TileChrome.Frozen(0x33, 0x33, 0x33);
    private static readonly Brush DarkDash = TileChrome.FrozenArgb(0x66, 0xE8, 0xE8, 0xE8), LightDash = TileChrome.FrozenArgb(0x66, 0x10, 0x10, 0x10);
    private static readonly Brush DarkHover = TileChrome.Frozen(0x2A, 0x2A, 0x2A), LightHover = TileChrome.FrozenArgb(0x14, 0x10, 0x10, 0x10);

    /// <summary>Text a step quieter than <see cref="Text"/> but louder than <see cref="Dim"/> (hints, high / low).</summary>
    public static Brush Soft => Light ? LightSoft : DarkSoft;

    /// <summary>Dashed outlines (an empty photo frame or folder slot).</summary>
    public static Brush Dash => Light ? LightDash : DarkDash;

    private static readonly Brush DarkPopup = TileChrome.Frozen(0x1A, 0x1A, 0x1A), LightPopup = TileChrome.Frozen(0xFF, 0xFF, 0xFF);

    /// <summary>An opaque card for popups next to a widget (the Notes editor).</summary>
    public static Brush Popup => Light ? LightPopup : DarkPopup;

    /// <summary>A tile under the pointer inside a card.</summary>
    public static Brush Hover => Light ? LightHover : DarkHover;

    /// <summary>The theme changed: every widget card is built again.</summary>
    public static event Action? Changed;

    public static void Apply(bool light)
    {
        if (light == Light) return;
        Light = light;
        Changed?.Invoke();
    }

    /// <summary>Reads Windows' app theme now and whenever the user's preferences change (call once, on the UI thread).</summary>
    public static void FollowWindows()
    {
        Apply(WindowsUsesLightTheme());
        if (s_following) return;
        s_following = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(WindowsUsesLightTheme()));
        };
    }

    private static bool WindowsUsesLightTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Personalize);
            return k?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary><see cref="TileChrome.Label"/> in the theme's dim colour (or <paramref name="brush"/>).</summary>
    public static TextBlock Label(string text, Brush? brush = null) => TileChrome.Label(text, brush ?? Dim);

    /// <summary><see cref="TileChrome.Body"/> in the theme's text colour (or <paramref name="brush"/>).</summary>
    public static TextBlock Body(string text, double size, FontWeight weight, Brush? brush = null) => TileChrome.Body(text, size, weight, brush ?? Text);

    /// <summary><see cref="TileChrome.Icon"/> stroked in the theme's text colour.</summary>
    public static FrameworkElement Icon(string name, double size) => TileChrome.Icon(name, size, Text);
}
