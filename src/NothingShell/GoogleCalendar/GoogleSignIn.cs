using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using NothingShell.Core.GoogleCalendar;
using NothingShell.Native;

namespace NothingShell.GoogleCalendar;

/// <summary>
/// The one-time Google sign-in (Plan 6): Google's page opens in the user's normal browser (through Explorer, so not
/// elevated); Google sends the browser back to a loopback port this listens on (a raw socket: no URL reservation
/// needed); the code is exchanged for tokens with PKCE. Every browser request gets a small page back; requests without
/// our <c>state</c> (the favicon, a stray tab) are ignored. Gives up after 5 minutes.
/// </summary>
public static class GoogleSignIn
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(5);

    /// <summary>The token (with a refresh token), or null and a short status for the Settings line.</summary>
    public static async Task<(GoogleToken? Token, string? Error)> RunAsync(HttpClient http, string clientId, string secret, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            var verifier = GoogleOAuth.NewVerifier();
            var state = GoogleOAuth.NewVerifier()[..32];
            var url = GoogleOAuth.AuthUrl(clientId, redirect, GoogleOAuth.Challenge(verifier), state);
            if (!UnelevatedShell.Open(url, null, (m, _) => Log.Info("google sign-in: " + m)))
                return (null, "THE BROWSER DID NOT OPEN");

            timeout.CancelAfter(Limit);
            var answer = new TaskCompletionSource<(string? Code, string? Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var reg = timeout.Token.Register(() => answer.TrySetCanceled());
            _ = AcceptLoop(listener, state, answer, timeout.Token);

            (string? Code, string? Error) result;
            try { result = await answer.Task; }
            catch (TaskCanceledException) { return (null, ct.IsCancellationRequested ? "CANCELLED" : "TIMED OUT, TRY AGAIN"); }
            if (result.Code is null)
            {
                Log.Info("google sign-in: refused in the browser");
                return (null, result.Error == "access_denied" ? "SIGN-IN CANCELLED IN THE BROWSER" : "GOOGLE REFUSED THE SIGN-IN");
            }

            using var response = await http.PostAsync(GoogleOAuth.TokenUrl,
                new FormUrlEncodedContent(GoogleOAuth.CodeForm(clientId, secret, result.Code, verifier, redirect)), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var token = response.IsSuccessStatusCode ? GoogleOAuth.ParseToken(body) : null;
            if (token?.RefreshToken is null)
            {
                Log.Info($"google sign-in: token exchange failed ({(int)response.StatusCode})");
                return (null, response.StatusCode == HttpStatusCode.Unauthorized ? "CHECK THE CLIENT ID AND SECRET" : "SIGN-IN FAILED, TRY AGAIN");
            }
            return (token, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or SocketException or IOException)
        {
            // The exception text could carry a URL; only its kind is logged.
            Log.Info("google sign-in: failed (" + e.GetType().Name + ")");
            return (null, ct.IsCancellationRequested ? "CANCELLED" : "OFFLINE? TRY AGAIN");
        }
        finally
        {
            timeout.Cancel();
            listener.Stop();
        }
    }

    private static async Task AcceptLoop(TcpListener listener, string state, TaskCompletionSource<(string?, string?)> answer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !answer.Task.IsCompleted)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { return; }
            _ = Serve(client, state, answer, ct); // one at a time would stall on a browser's idle pre-connection
        }
    }

    private static async Task Serve(TcpClient client, string state, TaskCompletionSource<(string?, string?)> answer, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var slow = CancellationTokenSource.CreateLinkedTokenSource(ct);
                slow.CancelAfter(TimeSpan.FromSeconds(30));
                var stream = client.GetStream();
                var line = await ReadLine(stream, slow.Token);
                var (code, gotState, error) = GoogleOAuth.ParseRedirect(line);
                var ours = gotState == state && (code is not null || error is not null);
                var page = !ours ? "Nothing Shell" : code is not null
                    ? "Nothing Shell is connected to Google Calendar. You can close this tab."
                    : "Sign-in was cancelled. You can close this tab.";
                var html = $"<!doctype html><meta charset=utf-8><title>Nothing Shell</title><body style=\"background:#000;color:#fff;font:16px sans-serif;display:grid;place-items:center;height:90vh\">{page}</body>";
                var bytes = Encoding.UTF8.GetBytes(html);
                var head = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), slow.Token);
                await stream.WriteAsync(bytes, slow.Token);
                if (ours) answer.TrySetResult((code, error));
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        }
    }

    // The request line only (up to the first CRLF), at most 8 KB.
    private static async Task<string> ReadLine(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var length = 0;
        while (length < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(length), ct);
            if (n == 0) break;
            length += n;
            var end = Array.IndexOf(buffer, (byte)'\n', 0, length);
            if (end >= 0) return Encoding.ASCII.GetString(buffer, 0, end).TrimEnd('\r');
        }
        return Encoding.ASCII.GetString(buffer, 0, length);
    }
}
