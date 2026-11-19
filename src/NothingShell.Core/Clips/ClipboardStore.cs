using System.Text.Json;

namespace NothingShell.Core.Clips;

/// <summary>
/// <c>%AppData%\NothingShell\clipboard.json</c>: the clipboard history (newest first), written atomically. Reads the
/// older text-only format (a list of strings) too. An unreadable file reads as empty. Contents are never logged.
/// </summary>
public sealed class ClipboardStore(string directory)
{
    private const string FileName = "clipboard.json";

    public IReadOnlyList<ClipItem> Load()
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path)) return [];
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var items = new List<ClipItem>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String) items.Add(ClipItem.OfText(e.GetString()!)); // Plan 4 text-only file
                else if (e.ValueKind == JsonValueKind.Object && e.Deserialize<ClipItem>(JsonFile.Options) is { } item
                         && (item.Text is not null || item.ImageId is not null))
                    items.Add(item);
            }
            return items;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<ClipItem> items) => JsonFile.WriteAtomic(directory, FileName, items);
}
