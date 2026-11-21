using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows.Threading;
using NothingShell.Core.GoogleCalendar;

namespace NothingShell.GoogleCalendar;

public enum CalendarStatus { NotSetUp, SignedOut, SignedIn }

/// <summary>
/// The user's Google Calendar for the Calendar widget (Plan 6, spec §6.5). Events come from every calendar switched on
/// in Google Calendar (user, 2026-11-21) and refresh every 15 minutes, plus when a widget becomes visible (at most once
/// per 2 minutes). The last events are kept encrypted (<see cref="GoogleVault"/>) and shown while offline. A refresh
/// token Google refuses for good signs the user out. Only counts and outcomes are logged: never titles, calendar ids,
/// tokens or the client's id and secret. Used on the UI thread; <see cref="Changed"/> is raised there.
/// </summary>
public sealed class GoogleCalendarService : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(15), Soonest = TimeSpan.FromMinutes(2);
    private const int MaxPages = 10;
    private static readonly HttpClient Http = CreateClient();
    private readonly GoogleVault _vault;
    private readonly DispatcherTimer _timer;
    private GoogleCredentials? _credentials;
    private string? _access;
    private DateTimeOffset _accessExpires;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private CancellationTokenSource? _signIn;
    private bool _refreshing, _refreshAgain, _disposed;
    // Bumped by sign-in, sign-out and Disconnect: an answer that arrives for an older generation is dropped (final
    // review M1: a Disconnect or reconnect while a refresh is on the wire).
    private int _generation;

    public GoogleCalendarService(string dataDir)
    {
        _vault = new GoogleVault(dataDir);
        _timer = new DispatcherTimer { Interval = Every };
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public CalendarStatus Status { get; private set; }
    public bool Connecting => _signIn is not null;
    public IReadOnlyList<CalEvent> Events { get; private set; } = [];

    /// <summary>The Settings status line ("CONNECTED · 3 CALENDARS", "OFFLINE · SHOWING SAVED EVENTS", …).</summary>
    public string StatusText { get; private set; } = "";

    /// <summary>The client ID saved earlier (prefills Settings after a sign-out).</summary>
    public string? SavedClientId => _credentials?.ClientId;

    /// <summary>A secret is saved for <see cref="SavedClientId"/>: Connect works with the secret box left empty.</summary>
    public bool HasSavedSecret => _credentials is { ClientSecret.Length: > 0 };

    /// <summary>
    /// The client to sign in with: the typed ID and secret, or (final review I1) the saved secret when the secret box is
    /// empty and the ID is the saved one. Null when something is missing.
    /// </summary>
    public static (string Id, string Secret)? ResolveClient(string clientId, string typedSecret, GoogleCredentials? saved)
    {
        var id = clientId.Trim();
        var secret = typedSecret.Trim();
        if (id.Length == 0) return null;
        if (secret.Length > 0) return (id, secret);
        return saved is { ClientSecret.Length: > 0 } s && s.ClientId == id ? (id, s.ClientSecret) : null;
    }

    public event Action? Changed;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("NothingShell/1.0");
        return c;
    }

    /// <summary>Loads the saved sign-in and events, then refreshes when signed in.</summary>
    public void Start()
    {
        _credentials = _vault.Load();
        Status = _credentials is null ? CalendarStatus.NotSetUp
            : _credentials.RefreshToken is null ? CalendarStatus.SignedOut : CalendarStatus.SignedIn;
        if (Status == CalendarStatus.SignedIn)
        {
            Events = _vault.LoadEvents() ?? [];
            StatusText = "CONNECTED";
            _timer.Start();
            _ = RefreshAsync();
        }
        Log.Info($"calendar: start ({Status})");
        Raise();
    }

    /// <summary>Signs in with the user's own OAuth client: the browser opens, the user approves, the events load.</summary>
    public async Task<bool> ConnectAsync(string typedId, string typedSecret)
    {
        if (_signIn is not null || _disposed) return false;
        if (ResolveClient(typedId, typedSecret, _credentials) is not { } client)
        {
            StatusText = typedId.Trim().Length == 0 ? "PASTE THE CLIENT ID FIRST" : "PASTE THE CLIENT SECRET FIRST";
            Raise();
            return false;
        }
        var (clientId, secret) = client;
        _signIn = new CancellationTokenSource();
        StatusText = "FINISH SIGNING IN IN YOUR BROWSER…";
        Raise();
        try
        {
            var (token, error) = await GoogleSignIn.RunAsync(Http, clientId, secret, _signIn.Token);
            if (_disposed) return false;
            if (token?.RefreshToken is null)
            {
                StatusText = error ?? "SIGN-IN FAILED";
                return false;
            }
            _generation++;
            _credentials = new GoogleCredentials(clientId, secret, token.RefreshToken);
            try { _vault.Save(_credentials); }
            catch (Exception e) { Log.Error("calendar: saving the sign-in failed (this session only)", e); }
            _access = token.AccessToken;
            _accessExpires = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            Status = CalendarStatus.SignedIn;
            StatusText = "CONNECTED";
            Log.Info("calendar: signed in");
            _timer.Start();
        }
        catch (Exception e)
        {
            Log.Error("calendar: sign-in failed unexpectedly", e);
            StatusText = "SIGN-IN FAILED";
            return false;
        }
        finally
        {
            _signIn.Dispose();
            _signIn = null;
            Raise();
        }
        await RefreshAsync();
        return true;
    }

    /// <summary>Stops a sign-in that is waiting for the browser.</summary>
    public void CancelConnect() => _signIn?.Cancel();

    /// <summary>A calendar widget became visible: refresh unless that happened in the last 2 minutes.</summary>
    public void RefreshSoon()
    {
        if (Status == CalendarStatus.SignedIn && DateTimeOffset.UtcNow - _lastRefresh > Soonest) _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (Status != CalendarStatus.SignedIn || _disposed) return;
        if (_refreshing) { _refreshAgain = true; return; }
        _refreshing = true;
        _refreshAgain = false;
        _lastRefresh = DateTimeOffset.UtcNow;
        var generation = _generation;
        try
        {
            var zone = TimeZoneInfo.Local;
            var (from, to) = CalendarFeed.Window(DateTimeOffset.Now);
            var ids = new List<string>();
            var readOnly = new HashSet<string>();
            string? page = null;
            for (var i = 0; i < MaxPages; i++)
            {
                var json = await GetAsync(CalendarFeed.CalendarListUrl(page), generation);
                if (json is null) return; // signed out meanwhile
                var (some, next) = CalendarFeed.ParseCalendarList(json);
                ids.AddRange(some);
                readOnly.UnionWith(CalendarFeed.ReadOnlyCalendars(json));
                if ((page = next) is null) break;
            }
            var events = new List<CalEvent>();
            var skipped = 0;
            foreach (var id in ids)
            {
                page = null;
                for (var i = 0; i < MaxPages; i++)
                {
                    var json = await GetAsync(CalendarFeed.EventsUrl(id, from, to, page), generation, skipUnreadable: true);
                    if (json is null) { skipped++; break; }
                    var (some, next) = CalendarFeed.ParseEvents(json, zone, id, mine: !readOnly.Contains(id));
                    events.AddRange(some);
                    if ((page = next) is null) break;
                }
                if (Status != CalendarStatus.SignedIn || generation != _generation) return;
            }
            if (_disposed || Status != CalendarStatus.SignedIn || generation != _generation) return;
            events.Sort((a, b) => a.Start.CompareTo(b.Start));
            Events = events;
            try { _vault.SaveEvents(events); }
            catch (Exception e) { Log.Error("calendar: cache write failed", e); }
            StatusText = ids.Count == 1 ? "CONNECTED · 1 CALENDAR" : $"CONNECTED · {ids.Count} CALENDARS";
            Log.Info($"calendar: refreshed ({ids.Count} calendars, {skipped} skipped, {events.Count} events)");
        }
        catch (ApiDisabledException)
        {
            // Signed in fine, but setup step 2 was skipped (final review I2): say so instead of "offline".
            Log.Info("calendar: the Google Calendar API is not enabled in the user's Cloud project");
            if (Status == CalendarStatus.SignedIn && generation == _generation) StatusText = "ENABLE THE GOOGLE CALENDAR API (STEP 2), THEN REFRESH";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // The exception text could carry a URL with a calendar id; only its kind is logged.
            Log.Info("calendar: refresh failed (" + e.GetType().Name + "), showing the saved events");
            if (Status == CalendarStatus.SignedIn && generation == _generation) StatusText = "OFFLINE · SHOWING SAVED EVENTS";
        }
        finally
        {
            _refreshing = false;
            Raise();
            // A refresh asked for meanwhile, or a new sign-in while an older refresh was on the wire, runs now.
            if ((_refreshAgain || generation != _generation) && Status == CalendarStatus.SignedIn && !_disposed) _ = RefreshAsync();
        }
    }

    private sealed class ApiDisabledException() : HttpRequestException("calendar api disabled");

    /// <summary>Signs out: the token is revoked (best effort), the saved sign-in and events are deleted.</summary>
    public async Task DisconnectAsync()
    {
        var refresh = _credentials?.RefreshToken;
        _generation++;
        _timer.Stop();
        _vault.Clear();
        _credentials = null;
        _access = null;
        Events = [];
        Status = CalendarStatus.NotSetUp;
        StatusText = "";
        Log.Info("calendar: disconnected");
        Raise();
        if (refresh is null) return;
        try
        {
            using var r = await Http.PostAsync(GoogleOAuth.RevokeUrl, new FormUrlEncodedContent([new("token", refresh)]));
            Log.Info($"calendar: revoke {(int)r.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { Log.Info("calendar: revoke failed (offline?)"); }
    }

    // GET with the access token; a 401 gets one fresh token and one retry. Null: signed out (or an older generation),
    // or (skipUnreadable) a calendar the account may no longer read (403 that is not a rate limit, 404, 410). A disabled
    // Calendar API throws ApiDisabledException; other failures (rate limits included: final review M3, so a calendar's
    // events are never dropped from the cache) throw HttpRequestException and keep the saved events.
    private async Task<string?> GetAsync(string url, int generation, bool skipUnreadable = false)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var access = await AccessTokenAsync(generation);
            if (access is null) return null;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var response = await Http.SendAsync(request);
            if (generation != _generation) return null;
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { _access = null; continue; }
            if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync();
            var reason = CalendarFeed.ErrorReason(await response.Content.ReadAsStringAsync());
            if (response.StatusCode == HttpStatusCode.Forbidden && CalendarFeed.IsApiDisabled(reason)) throw new ApiDisabledException();
            if (skipUnreadable && !CalendarFeed.IsRateLimited(reason)
                && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone) return null;
            throw new HttpRequestException($"calendar api {(int)response.StatusCode}");
        }
        throw new HttpRequestException("calendar api 401");
    }

    private async Task<string?> AccessTokenAsync(int generation)
    {
        if (_access is not null && _accessExpires > DateTimeOffset.UtcNow.AddMinutes(1)) return _access;
        if (_credentials is not { RefreshToken: { } refresh } c) return null;
        using var response = await Http.PostAsync(GoogleOAuth.TokenUrl,
            new FormUrlEncodedContent(GoogleOAuth.RefreshForm(c.ClientId, c.ClientSecret, refresh)));
        var body = await response.Content.ReadAsStringAsync();
        if (generation != _generation) return null; // disconnected or signed in again meanwhile
        if (!response.IsSuccessStatusCode)
        {
            if (GoogleOAuth.IsInvalidGrant(body) || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                SignedOut();
                return null;
            }
            throw new HttpRequestException($"token refresh {(int)response.StatusCode}");
        }
        if (GoogleOAuth.ParseToken(body) is not { } token) throw new HttpRequestException("token refresh: bad response");
        _access = token.AccessToken;
        _accessExpires = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
        return _access;
    }

    // Google refused the refresh token (revoked, expired, client deleted): keep the client ID and secret so Connect is
    // one click, drop the token and the events.
    private void SignedOut()
    {
        Log.Info("calendar: Google refused the saved sign-in, signed out");
        _generation++;
        _timer.Stop();
        _access = null;
        if (_credentials is not null)
        {
            _credentials = _credentials with { RefreshToken = null };
            try { _vault.Save(_credentials); _vault.SaveEvents([]); }
            catch (Exception e) { Log.Error("calendar: vault write failed", e); }
        }
        Events = [];
        Status = CalendarStatus.SignedOut;
        StatusText = "SIGNED OUT · CONNECT AGAIN";
    }

    private void Raise()
    {
        try { Changed?.Invoke(); }
        catch (Exception e) { Log.Error("calendar: Changed handler failed", e); }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _signIn?.Cancel();
    }
}
