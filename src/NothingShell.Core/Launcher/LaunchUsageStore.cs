using System.Text.Json;

namespace NothingShell.Core.Launcher;

/// <summary>
/// Launch history for the launcher's "Most used" (spec §6.2): <c>launcher-usage.json</c> in the data folder.
/// Capped at <see cref="MaxEntries"/>; the least recently launched entries go first. A missing or corrupt file
/// reads as empty (history is a convenience, never worth failing over).
/// </summary>
public sealed class LaunchUsageStore(string directory)
{
    public const string FileName = "launcher-usage.json";
    public const int MaxEntries = 500;

    public IReadOnlyDictionary<string, LaunchUsage> Load()
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path)) return new Dictionary<string, LaunchUsage>();
            var list = JsonSerializer.Deserialize<List<LaunchUsage>>(File.ReadAllText(path), JsonFile.Options) ?? [];
            var map = new Dictionary<string, LaunchUsage>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in list)
                if (!string.IsNullOrEmpty(u?.Id)) map[u.Id] = u;
            return map;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, LaunchUsage>();
        }
    }

    public void Record(string id, DateTimeOffset now)
    {
        var map = new Dictionary<string, LaunchUsage>(Load(), StringComparer.OrdinalIgnoreCase);
        map[id] = map.TryGetValue(id, out var u) ? u with { Count = u.Count + 1, LastLaunched = now } : new LaunchUsage(id, 1, now);
        var kept = map.Values.OrderByDescending(x => x.LastLaunched).Take(MaxEntries).ToList();
        JsonFile.WriteAtomic(directory, FileName, kept);
    }
}
