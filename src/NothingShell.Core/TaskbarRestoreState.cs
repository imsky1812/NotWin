using System.Text.Json;
using System.Text.Json.Serialization;

namespace NothingShell.Core;

public sealed record TaskbarRestoreState(bool WasAutoHide);

// Source-generated so the Native AOT watchdog can read the file without reflection.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true)]
[JsonSerializable(typeof(TaskbarRestoreState))]
internal sealed partial class RestoreStateJsonContext : JsonSerializerContext;

public sealed class TaskbarRestoreStore(string directory)
{
    private const string FileName = "taskbar-restore.json";

    public bool TryLoad(out TaskbarRestoreState state)
    {
        state = new TaskbarRestoreState(false);
        var path = Path.Combine(directory, FileName);
        try
        {
            if (!File.Exists(path)) return false;
            var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), RestoreStateJsonContext.Default.TaskbarRestoreState);
            if (loaded is null) return false;
            state = loaded;
            return true;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Save(TaskbarRestoreState state) => JsonFile.WriteAtomic(directory, FileName, state, RestoreStateJsonContext.Default.TaskbarRestoreState);

    public void Delete() => File.Delete(Path.Combine(directory, FileName));
}
