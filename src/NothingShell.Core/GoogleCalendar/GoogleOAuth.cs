using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NothingShell.Core.GoogleCalendar;

/// <summary>An OAuth token response: the access token, the refresh token (only on the first exchange) and its lifetime.</summary>
public sealed record GoogleToken(string AccessToken, string? RefreshToken, int ExpiresIn);

/// <summary>
/// Google's desktop-app OAuth flow (Plan 6, spec §6.5): PKCE S256, a loopback redirect, the read-only calendar scope,
/// <c>access_type=offline</c> + <c>prompt=consent</c> so a refresh token always comes back. Pure rules; the app does
/// the network and the browser. Nothing here is ever logged (tokens, the client id and secret are private).
/// </summary>
public static class GoogleOAuth
{
    public const string Scope = "https://www.googleapis.com/auth/calendar.readonly";
    public const string TokenUrl = "https://oauth2.googleapis.com/token";
    public const string RevokeUrl = "https://oauth2.googleapis.com/revoke";
    private const string Unreserved = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    /// <summary>A PKCE code verifier: 64 unreserved characters (cryptographically random unless a test passes a seed).</summary>
    public static string NewVerifier(Random? rng = null)
    {
        var chars = new char[64];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Unreserved[rng?.Next(Unreserved.Length) ?? RandomNumberGenerator.GetInt32(Unreserved.Length)];
        return new string(chars);
    }

    /// <summary>The S256 challenge: base64url(SHA-256(verifier)) without padding (RFC 7636).</summary>
    public static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string AuthUrl(string clientId, string redirect, string challenge, string state) =>
        "https://accounts.google.com/o/oauth2/v2/auth?" + Query(
            ("client_id", clientId), ("redirect_uri", redirect), ("response_type", "code"), ("scope", Scope),
            ("code_challenge", challenge), ("code_challenge_method", "S256"), ("state", state),
            ("access_type", "offline"), ("prompt", "consent"));

    public static IReadOnlyList<KeyValuePair<string, string>> CodeForm(string clientId, string secret, string code, string verifier, string redirect) =>
    [
        new("grant_type", "authorization_code"), new("code", code), new("code_verifier", verifier),
        new("redirect_uri", redirect), new("client_id", clientId), new("client_secret", secret),
    ];

    public static IReadOnlyList<KeyValuePair<string, string>> RefreshForm(string clientId, string secret, string refreshToken) =>
    [
        new("grant_type", "refresh_token"), new("refresh_token", refreshToken), new("client_id", clientId), new("client_secret", secret),
    ];

    /// <summary>The token response, or null when it has no access token or is not JSON.</summary>
    public static GoogleToken? ParseToken(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var access = Str(root, "access_token");
            if (string.IsNullOrEmpty(access)) return null;
            var expires = root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : 3600;
            var refresh = Str(root, "refresh_token");
            return new GoogleToken(access, string.IsNullOrEmpty(refresh) ? null : refresh, expires);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Whether Google refused the refresh token for good (revoked, expired): the user must sign in again.</summary>
    public static bool IsInvalidGrant(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && Str(doc.RootElement, "error") == "invalid_grant";
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// The browser's request line on the loopback redirect (<c>GET /?code=..&amp;state=.. HTTP/1.1</c>): its code, state
    /// and error. Anything else (the favicon, garbage) gives three nulls.
    /// </summary>
    public static (string? Code, string? State, string? Error) ParseRedirect(string requestLine)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length < 2 || parts[0] != "GET") return (null, null, null);
        var q = parts[1].IndexOf('?');
        if (q < 0) return (null, null, null);
        string? code = null, state = null, error = null;
        foreach (var pair in parts[1][(q + 1)..].Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string value;
            try { value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')); }
            catch (UriFormatException) { continue; }
            switch (pair[..eq])
            {
                case "code": code = value; break;
                case "state": state = value; break;
                case "error": error = value; break;
            }
        }
        return (code, state, error);
    }

    internal static string Query(params (string Key, string Value)[] pairs) =>
        string.Join("&", pairs.Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value)));

    internal static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
