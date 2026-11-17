using System.Text.Json;

namespace NothingShell.Core;

public sealed class SettingsStore(string directory)
{
    private const string FileName = "settings.json";

    public ShellSettings Load()
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return new ShellSettings();
        try
        {
            return JsonSerializer.Deserialize<ShellSettings>(File.ReadAllText(path), JsonFile.Options)
                   ?? throw new JsonException("null settings");
        }
        catch (JsonException)
        {
            File.Move(path, path + ".bad", overwrite: true);
            return new ShellSettings();
        }
    }

    public void Save(ShellSettings settings) => JsonFile.WriteAtomic(directory, FileName, settings);
}
