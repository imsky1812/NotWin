#if DEBUG
using System.Windows;
using System.Windows.Media;

namespace NothingShell.Theme;

/// <summary>Debug-only startup check that every Font.* token resolves to its bundled family (logs one line per token).</summary>
internal static class FontCheck
{
    private static readonly (string Key, string Family)[] Expected =
    [
        ("Font.Grotesk", "Space Grotesk"),
        ("Font.Mono", "Space Mono"),
        ("Font.Geist", "Geist"), // bundled, no longer used (Nothing design pass)
        ("Font.GeistMono", "Geist Mono"),
        ("Font.Serif", "DM Serif Display"),
    ];

    public static void Run(ResourceDictionary resources)
    {
        try
        {
            var bundled = Fonts.GetFontFamilies(new Uri("pack://application:,,,/"), "./Theme/Fonts/")
                .SelectMany(f => f.FamilyNames.Values)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Log.Info($"fonts bundled: {string.Join(", ", bundled.Order())}");

            var failures = 0;
            foreach (var (key, family) in Expected)
            {
                var ff = resources[key] as FontFamily;
                GlyphTypeface? glyphs = null;
                var resolved = ff is not null &&
                    new Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out glyphs) &&
                    glyphs.FamilyNames.Values.Contains(family, StringComparer.OrdinalIgnoreCase);
                if (!resolved || !bundled.Contains(family)) failures++;
                Log.Info($"font {key}: {(resolved ? "ok" : "MISSING")} family='{family}' bundled={bundled.Contains(family)} " +
                         $"face='{glyphs?.FaceNames.Values.FirstOrDefault()}' weight={glyphs?.Weight} uri={glyphs?.FontUri}");
            }
            Log.Info(failures == 0 ? "font check: all 5 families resolve" : $"font check: {failures} FAILED");
        }
        catch (Exception e)
        {
            Log.Error("font check failed", e);
        }
    }
}
#endif
