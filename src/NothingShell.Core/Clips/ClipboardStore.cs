using System.Text.Json;

namespace NothingShell.Core.Clips;

/// <summary>
/// <c>%AppData%\NothingShell\clipboard.json</c>: the clipboard history (newest first), written atomically.
/// An unreadable file reads as empty. The text itself is never logged.
/// </summary>
public sealed class ClipboardStore(string directory)
{
    private const string FileName = "clipboard.json";

    public IReadOnlyList<string> Load()
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path), JsonFile.Options)?.Where(s => s is not null).ToList() ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<string> items) => JsonFile.WriteAtomic(directory, FileName, items);
}
