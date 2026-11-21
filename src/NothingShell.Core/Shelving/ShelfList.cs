using System.Text.Json;

namespace NothingShell.Core.Shelving;

/// <summary>
/// The notch's file shelf (spec §6.4): references to dropped files, not copies; up to 20, no repeats (paths compared
/// ignoring case); files that no longer exist are dropped on load.
/// </summary>
public sealed class ShelfList(int cap = 20)
{
    private readonly List<string> _paths = [];

    public int Cap => cap;
    public IReadOnlyList<string> Paths => _paths;

    public bool Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || _paths.Count >= cap) return false;
        if (_paths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase))) return false;
        _paths.Add(path);
        return true;
    }

    public void Remove(int index)
    {
        if (index >= 0 && index < _paths.Count) _paths.RemoveAt(index);
    }

    public static ShelfList From(IEnumerable<string>? saved, Func<string, bool> exists, int cap = 20)
    {
        var s = new ShelfList(cap);
        foreach (var p in saved ?? [])
            if (p is not null && exists(p)) s.Add(p);
        return s;
    }
}

/// <summary><c>%AppData%\NothingShell\shelf.json</c>: the shelf's paths. Never logged.</summary>
public sealed class ShelfStore(string directory)
{
    private const string FileName = "shelf.json";

    public IReadOnlyList<string> Load()
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path), JsonFile.Options) ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<string> paths) => JsonFile.WriteAtomic(directory, FileName, paths);
}
