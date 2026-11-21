using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NotWin.Core.GoogleCalendar;

namespace NotWin.GoogleCalendar;

/// <summary>The user's own Google OAuth client (typed in Settings) and, once signed in, the refresh token.</summary>
public sealed record GoogleCredentials(string ClientId, string ClientSecret, string? RefreshToken);

/// <summary>
/// Plan 6 storage (spec §6.5: encrypted for the current Windows user). <c>google.dat</c> holds the credentials,
/// <c>calendar.dat</c> the last events (titles are private). Each file is DPAPI CurrentUser-protected UTF-8 JSON; a file
/// that is missing, corrupt or not ours reads as null. Writes go through a temp file, so a crash never leaves half a file.
/// </summary>
public sealed class GoogleVault(string dir)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NothingShell.Google") /* kept: older sign-ins decrypt with it */;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private string CredentialsPath => Path.Combine(dir, "google.dat");
    private string EventsPath => Path.Combine(dir, "calendar.dat");

    public GoogleCredentials? Load() =>
        Read<GoogleCredentials>(CredentialsPath) is { ClientId.Length: > 0, ClientSecret.Length: > 0 } c ? c : null;

    public void Save(GoogleCredentials credentials) => Write(CredentialsPath, credentials);

    public IReadOnlyList<CalEvent>? LoadEvents() =>
        Read<List<CalEvent>>(EventsPath) is { } list && list.All(e => e?.Title is not null) ? list : null;

    public void SaveEvents(IReadOnlyList<CalEvent> events) => Write(EventsPath, events);

    public void Clear()
    {
        foreach (var path in new[] { CredentialsPath, EventsPath })
        {
            try { File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Error("google vault: delete failed", e); }
        }
    }

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(plain, Json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(dir);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value, Json), Entropy, DataProtectionScope.CurrentUser));
        File.Move(temp, path, overwrite: true);
    }
}
