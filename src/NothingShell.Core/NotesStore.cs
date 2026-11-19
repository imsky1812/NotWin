using System.Text;

namespace NothingShell.Core;

/// <summary>
/// The notch's one plain-text note (spec §6.4): <c>%AppData%\NothingShell\notes.txt</c>, UTF-8, written through a temp
/// file and an atomic replace so a crash never leaves it half written. The text is never logged.
/// </summary>
public sealed class NotesStore(string directory)
{
    private const string FileName = "notes.txt";

    public string Load()
    {
        try
        {
            var path = Path.Combine(directory, FileName);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    public void Save(string text)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, FileName);
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                w.Write(text);
                w.Flush();
                fs.Flush(flushToDisk: true);
            }
            if (File.Exists(target)) File.Replace(temp, target, null);
            else File.Move(temp, target);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
