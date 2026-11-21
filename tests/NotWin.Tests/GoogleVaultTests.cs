using System.IO;
using NotWin.Core.GoogleCalendar;
using NotWin.GoogleCalendar;

namespace NotWin.Tests;

public sealed class GoogleVaultTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ns-vault-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void Vault_RoundTripsCredentials()
    {
        var v = new GoogleVault(_dir);
        Assert.Null(v.Load());
        v.Save(new GoogleCredentials("id", "secret", "refresh"));
        Assert.Equal(new GoogleCredentials("id", "secret", "refresh"), new GoogleVault(_dir).Load());
        // Encrypted on disk: the secret never appears as plain text.
        Assert.DoesNotContain("secret", File.ReadAllText(Path.Combine(_dir, "google.dat")));
    }

    [Fact]
    public void Vault_RoundTripsEvents()
    {
        var v = new GoogleVault(_dir);
        Assert.Null(v.LoadEvents());
        var events = new[] { new CalEvent("Dentist", new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.FromHours(5.5)), new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(5.5)), false) };
        v.SaveEvents(events);
        Assert.Equal(events, new GoogleVault(_dir).LoadEvents());
        Assert.DoesNotContain("Dentist", File.ReadAllText(Path.Combine(_dir, "calendar.dat")));
    }

    [Fact]
    public void Vault_CorruptFile_IsNull()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "google.dat"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(_dir, "calendar.dat"), []);
        Assert.Null(new GoogleVault(_dir).Load());
        Assert.Null(new GoogleVault(_dir).LoadEvents());
    }

    [Fact]
    public void Vault_PlainJsonFile_IsNull()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "google.dat"), """{"clientId":"id","clientSecret":"s","refreshToken":"r"}""");
        Assert.Null(new GoogleVault(_dir).Load());
    }

    [Fact]
    public void Vault_Clear_RemovesBoth()
    {
        var v = new GoogleVault(_dir);
        v.Save(new GoogleCredentials("id", "s", null));
        v.SaveEvents([]);
        v.Clear();
        Assert.False(File.Exists(Path.Combine(_dir, "google.dat")));
        Assert.False(File.Exists(Path.Combine(_dir, "calendar.dat")));
        Assert.Null(v.Load());
    }

    // Final review I1: after Google signs you out, Connect reuses the saved secret for the same client ID.
    [Fact]
    public void ResolveClient_FallsBackToTheSavedSecret()
    {
        var saved = new GoogleCredentials("id.apps", "saved-secret", null);
        Assert.Equal(("id.apps", "typed"), GoogleCalendarService.ResolveClient(" id.apps ", " typed ", saved));
        Assert.Equal(("id.apps", "saved-secret"), GoogleCalendarService.ResolveClient("id.apps", "", saved));
        Assert.Null(GoogleCalendarService.ResolveClient("other.apps", "", saved)); // a different client needs its own secret
        Assert.Null(GoogleCalendarService.ResolveClient("id.apps", "", null));
        Assert.Null(GoogleCalendarService.ResolveClient("", "typed", saved));
    }
}
