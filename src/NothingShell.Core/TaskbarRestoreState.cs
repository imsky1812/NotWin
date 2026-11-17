using System.Text.Json;

namespace NothingShell.Core;

public sealed record TaskbarRestoreState(bool WasAutoHide);

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
            var loaded = JsonSerializer.Deserialize<TaskbarRestoreState>(File.ReadAllText(path), JsonFile.Options);
            if (loaded is null) return false;
            state = loaded;
            return true;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Save(TaskbarRestoreState state) => JsonFile.WriteAtomic(directory, FileName, state);

    public void Delete() => File.Delete(Path.Combine(directory, FileName));
}
