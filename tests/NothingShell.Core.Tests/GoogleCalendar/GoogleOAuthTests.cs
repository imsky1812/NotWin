using NothingShell.Core.GoogleCalendar;

namespace NothingShell.Core.Tests.GoogleCalendar;

public sealed class GoogleOAuthTests
{
    [Fact]
    public void Challenge_MatchesRfc7636Example() =>
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", GoogleOAuth.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void Verifier_Is64UrlSafeChars()
    {
        var v = GoogleOAuth.NewVerifier(new Random(1));
        Assert.Equal(64, v.Length);
        Assert.All(v, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~'));
        Assert.NotEqual(v, GoogleOAuth.NewVerifier(new Random(2)));
    }

    [Fact]
    public void AuthUrl_HasScopePkceOfflineAndConsent()
    {
        var url = GoogleOAuth.AuthUrl("id.apps", "http://127.0.0.1:5000/", "chal", "st8");
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        Assert.Contains("client_id=id.apps", url);
        Assert.Contains("redirect_uri=" + Uri.EscapeDataString("http://127.0.0.1:5000/"), url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("scope=" + Uri.EscapeDataString(GoogleOAuth.Scope), url);
        Assert.Contains("code_challenge=chal", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("state=st8", url);
        Assert.Contains("access_type=offline", url);
        Assert.Contains("prompt=consent", url);
        Assert.Equal("https://www.googleapis.com/auth/calendar.readonly", GoogleOAuth.Scope);
    }

    [Fact]
    public void Forms_CarryTheRightFields()
    {
        var code = GoogleOAuth.CodeForm("id", "sec", "c0de", "ver", "http://127.0.0.1:1/").ToDictionary();
        Assert.Equal("authorization_code", code["grant_type"]);
        Assert.Equal("c0de", code["code"]);
        Assert.Equal("ver", code["code_verifier"]);
        Assert.Equal("http://127.0.0.1:1/", code["redirect_uri"]);
        Assert.Equal("id", code["client_id"]);
        Assert.Equal("sec", code["client_secret"]);
        var refresh = GoogleOAuth.RefreshForm("id", "sec", "rt").ToDictionary();
        Assert.Equal("refresh_token", refresh["grant_type"]);
        Assert.Equal("rt", refresh["refresh_token"]);
    }

    [Fact]
    public void ParseToken_ReadsFields()
    {
        var t = GoogleOAuth.ParseToken("""{"access_token":"at","expires_in":3599,"refresh_token":"rt","scope":"x","token_type":"Bearer"}""");
        Assert.Equal(new GoogleToken("at", "rt", 3599), t);
        Assert.Equal(new GoogleToken("at", null, 3599), GoogleOAuth.ParseToken("""{"access_token":"at","expires_in":3599}"""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"error":"invalid_grant"}""")]
    [InlineData("""{"access_token":""}""")]
    public void ParseToken_Garbage_IsNull(string json) => Assert.Null(GoogleOAuth.ParseToken(json));

    [Fact]
    public void IsInvalidGrant_True_ForInvalidGrant()
    {
        Assert.True(GoogleOAuth.IsInvalidGrant("""{"error":"invalid_grant","error_description":"Token has been expired or revoked."}"""));
        Assert.False(GoogleOAuth.IsInvalidGrant("""{"error":"invalid_client"}"""));
        Assert.False(GoogleOAuth.IsInvalidGrant("garbage"));
    }

    [Fact]
    public void ParseRedirect_CodeAndState()
    {
        var r = GoogleOAuth.ParseRedirect("GET /?state=st8&code=4%2F0Ab&scope=https%3A%2F%2Fwww.googleapis.com HTTP/1.1");
        Assert.Equal(("4/0Ab", "st8", (string?)null), r);
    }

    [Fact]
    public void ParseRedirect_Error() =>
        Assert.Equal(((string?)null, "s", "access_denied"), GoogleOAuth.ParseRedirect("GET /?error=access_denied&state=s HTTP/1.1"));

    [Theory]
    [InlineData("GET /favicon.ico HTTP/1.1")]
    [InlineData("")]
    [InlineData("garbage")]
    public void ParseRedirect_Favicon_HasNothing(string line) =>
        Assert.Equal(((string?)null, (string?)null, (string?)null), GoogleOAuth.ParseRedirect(line));
}
