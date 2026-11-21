using System.Text.Json;

namespace NotWin.Core;

public sealed class SettingsStore(string directory)
{
    private const string FileName = "settings.json";
    private const int ReadAttempts = 3;

    // Set when Load could not read an existing file for a transient/permission reason.
    // Defaults were returned, so Save must not clobber the user's real settings.
    private bool _loadFailed;

    /// <summary>False when the last Load could not read an existing file (defaults were returned and Save is a no-op).</summary>
    public bool LastLoadSucceeded => !_loadFailed;

    public ShellSettings Load()
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            _loadFailed = false;
            return new ShellSettings();
        }

        string text;
        try
        {
            text = ReadWithRetry(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _loadFailed = true;
            return new ShellSettings();
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<ShellSettings>(text, JsonFile.Options)
                         ?? throw new JsonException("null settings");
            _loadFailed = false;
            // A hand-edited or older file may say "pinnedApps": null; callers rely on a list.
            return loaded.PinnedApps is null ? loaded with { PinnedApps = [] } : loaded;
        }
        catch (JsonException)
        {
            try { File.Move(path, path + ".bad", overwrite: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            _loadFailed = false;
            return new ShellSettings();
        }
    }

    /// <summary>No-op while the last Load failed to read an existing file (protects it from being overwritten).</summary>
    public void Save(ShellSettings settings)
    {
        if (_loadFailed) return;
        JsonFile.WriteAtomic(directory, FileName, settings);
    }

    private static string ReadWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (attempt < ReadAttempts) { Thread.Sleep(50); }
        }
    }
}
