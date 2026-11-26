using System.Globalization;

namespace NotWin.Core.Widgets;

/// <summary>
/// The app folder's face, "Peek + name" (user 2026-11-27, after Nothing OS folders): the apps inside as icons, the
/// folder's name and how many apps it holds.
/// </summary>
public static class FolderFace
{
    public const int MaxName = 24;

    /// <summary>
    /// Icons shown and the count behind a "+N" circle: a 1×1 folder has room for three circles, a 2×2 for six; when
    /// there are more apps, the last circle becomes "+N".
    /// </summary>
    public static (int Shown, int More) Slots(int apps, bool big)
    {
        var room = big ? 6 : 3;
        return apps <= room ? (apps, 0) : (room - 1, apps - (room - 1));
    }

    public static string CountLabel(int apps) => apps switch
    {
        0 => "ADD APPS",
        1 => "1 APP",
        _ => apps.ToString(CultureInfo.InvariantCulture) + " APPS",
    };

    /// <summary>The name shown (upper case, at most <see cref="MaxName"/> characters); "FOLDER" until one is set.</summary>
    public static string Name(string? title)
    {
        var t = title?.Trim();
        if (string.IsNullOrEmpty(t)) return "FOLDER";
        t = t.ToUpperInvariant();
        return t.Length > MaxName ? t[..MaxName] : t;
    }
}
