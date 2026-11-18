using System.Globalization;
using System.Text;

namespace NothingShell.Core.Launcher;

/// <summary>
/// Scores and orders launcher results. Order (spec §6.2, strict by user decision): kind group
/// (apps → settings → files/folders), then score, then launch count, then most recent launch, then name.
/// </summary>
public static class SearchRanker
{
    public const int MaxResults = 6;

    private static readonly char[] WordSeparators = [' ', '-', '_', '.', '\t'];

    /// <summary>100 exact · 90 name prefix · 75 word prefix · 65 initials (2+ chars) · 60 keyword prefix · 50 substring · 0 none.</summary>
    public static int Score(string query, LaunchItem item)
    {
        var q = Normalize(query);
        if (q.Length == 0) return 0;
        var name = Normalize(item.Name);

        if (name == q) return 100;
        if (name.StartsWith(q, StringComparison.Ordinal)) return 90;

        var words = name.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(q, StringComparison.Ordinal))) return 75;

        if (q.Length >= 2 && words.Length >= 2)
        {
            var initials = string.Concat(words.Select(w => w[0]));
            if (initials.StartsWith(q, StringComparison.Ordinal)) return 65;
        }

        if (item.Keywords.Any(k => Normalize(k).StartsWith(q, StringComparison.Ordinal))) return 60;
        if (name.Contains(q, StringComparison.Ordinal)) return 50;
        return 0;
    }

    public static IReadOnlyList<LaunchItem> Rank(string query, IEnumerable<LaunchItem> items,
        IReadOnlyDictionary<string, LaunchUsage> usage, int max = MaxResults)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return items
            .Select(i => (Item: i, Score: Score(query, i)))
            .Where(x => x.Score > 0)
            .OrderBy(x => Group(x.Item.Kind))
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => usage.TryGetValue(x.Item.Id, out var u) ? u.Count : 0)
            .ThenByDescending(x => usage.TryGetValue(x.Item.Id, out var u) ? u.LastLaunched : DateTimeOffset.MinValue)
            .ThenBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(x => x.Item)
            .ToList();
    }

    private static int Group(LaunchKind k) => k switch
    {
        LaunchKind.App => 0,
        LaunchKind.Setting => 1,
        _ => 2, // files and folders share a group
    };

    /// <summary>Trimmed, lower-invariant, diacritics removed ("Café" → "cafe").</summary>
    internal static string Normalize(string s)
    {
        var d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
