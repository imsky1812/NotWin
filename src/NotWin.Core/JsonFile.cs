using System.Text.Json;

namespace NotWin.Core;

internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static void WriteAtomic<T>(string directory, string fileName, T value)
        => WriteAtomic(directory, fileName, JsonSerializer.Serialize(value, Options));

    public static void WriteAtomic<T>(string directory, string fileName, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => WriteAtomic(directory, fileName, JsonSerializer.Serialize(value, typeInfo));

    private static void WriteAtomic(string directory, string fileName, string json)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, fileName);
        var temp = Path.Combine(directory, fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            // Flush to disk before the replace: after a power loss the file is either the old or the new
            // content, never empty (taskbar-restore.json holds the user's original auto-hide setting).
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs, new System.Text.UTF8Encoding(false)))
            {
                w.Write(json);
                w.Flush();
                fs.Flush(flushToDisk: true);
            }
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
