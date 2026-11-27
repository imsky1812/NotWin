using System.Text.Json;

namespace NotWin.Core.Launcher;

/// <summary><c>%AppData%\NotWin\programs.json</c>: the programs the user added from their PC (Plan 14). Paths, never logged.</summary>
public sealed class CustomAppsStore(string directory)
{
    private const string FileName = "programs.json";

    public IReadOnlyList<CustomApp> Load()
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<CustomApp>>(File.ReadAllText(path), JsonFile.Options) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<CustomApp> apps)
    {
        try { JsonFile.WriteAtomic(directory, FileName, apps); }
        catch (Exception) { /* a transient IO error just means the list is not persisted this time */ }
    }
}
