using System.Text.Json;

namespace NothingShell.Core;

internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static void WriteAtomic<T>(string directory, string fileName, T value)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, fileName);
        var temp = Path.Combine(directory, fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
