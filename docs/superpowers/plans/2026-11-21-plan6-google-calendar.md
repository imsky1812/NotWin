# Plan 6: Google Calendar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Calendar widget shows the user's Google Calendar events: dots under days with events (2×2), the next
event (2×1). Sign-in and the guided setup live in Settings.

**Architecture:**
- Pure rules live in Core (`NothingShell.Core.GoogleCalendar`):
  - OAuth URLs, form bodies and response parsing;
  - Calendar API parsing;
  - which days have dots;
  - the next event and its label.
- The app layer holds:
  - a DPAPI vault;
  - a loopback sign-in;
  - a `GoogleCalendarService` shaped like `WeatherService` (timer, encrypted cache, `Changed` event).
- Settings and the widget consume the service.

**Tech Stack:** .NET 10, WPF, `HttpClient`, `System.Text.Json`, `System.Security.Cryptography.ProtectedData` (part of
the Windows Desktop runtime), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-21-google-calendar-design.md` (parent: `2026-11-17-nothing-shell-design.md` §6.5)

## Global Constraints
- Scope exactly `https://www.googleapis.com/auth/calendar.readonly`.
- No new NuGet packages.
- **Never logged:** event titles, calendar ids or names, the email, tokens, the client ID or secret.
- Refresh every 15 min; a widget-triggered refresh at most once per 2 min.
- Fetch window: 7 days before the 1st of the current month to now + 60 days.
- Files in `%AppData%\NothingShell`: `google.dat` (credentials and refresh token) and `calendar.dat` (event cache),
  both DPAPI CurrentUser.
- Implementers never start or stop the installed shell and never run the installer.

## Review Focus
1. **Event times:** an all-day event on the last day of a month, and a timed event crossing midnight, each dot the
   right days (`DaysWithEvents` tests).
2. **Bad responses:** an empty or garbage response, or a missing `items`, gives an empty result, never an exception
   (parse tests).
3. **Loopback requests:** a browser request without `code` (a favicon request or `error=access_denied`) must not end
   the sign-in wrongly (`ParseRedirect` tests).
4. **Revoked refresh token** (`invalid_grant`): signs out cleanly instead of retrying forever (`IsInvalidGrant` test).
5. **Corrupt store files:** a corrupt or foreign `google.dat` or `calendar.dat` is treated as missing (vault test).

---

### Task 1: OAuth rules (Core)

**Files:** Create `src/NothingShell.Core/GoogleCalendar/GoogleOAuth.cs`; Test `tests/NothingShell.Core.Tests/GoogleCalendar/GoogleOAuthTests.cs`

**Interfaces — Produces:**
- `record GoogleToken(string AccessToken, string? RefreshToken, int ExpiresIn)`
- `static class GoogleOAuth`:
  - `const string Scope`
  - `string NewVerifier(Random? rng = null)`: 64 URL-safe characters
  - `string Challenge(string verifier)`: base64url of SHA-256, no padding
  - `string AuthUrl(string clientId, string redirect, string challenge, string state)`
  - `IReadOnlyList<KeyValuePair<string,string>> CodeForm(clientId, secret, code, verifier, redirect)`
  - `IReadOnlyList<KeyValuePair<string,string>> RefreshForm(clientId, secret, refreshToken)`
  - `GoogleToken? ParseToken(string json)`
  - `bool IsInvalidGrant(string json)`
  - `(string? Code, string? State, string? Error) ParseRedirect(string requestLine)`: takes `GET /?code=..&state=.. HTTP/1.1`

- [ ] Tests:
  - `Challenge_MatchesRfc7636Example`: the verifier `dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk` gives `E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM`.
  - `Verifier_Is64UrlSafeChars`.
  - `AuthUrl_HasScopePkceOfflineAndConsent`: contains `code_challenge_method=S256`, `access_type=offline`,
    `prompt=consent`, and `scope=` with the URL-encoded `Scope`.
  - `ParseToken_ReadsFields`.
  - `ParseToken_Garbage_IsNull`.
  - `IsInvalidGrant_True_ForInvalidGrant`.
  - `ParseRedirect_CodeAndState`, `ParseRedirect_Error`, and `ParseRedirect_Favicon_HasNothing`.
- [ ] Run them, see them fail, implement, see them pass. Commit `feat(calendar): OAuth PKCE rules`.

### Task 2: Calendar feed rules (Core)

**Files:** Create `src/NothingShell.Core/GoogleCalendar/CalendarFeed.cs`; Test `tests/NothingShell.Core.Tests/GoogleCalendar/CalendarFeedTests.cs`

**Interfaces — Produces:**
- `record CalEvent(string Title, DateTimeOffset Start, DateTimeOffset End, bool AllDay)`: all-day Start and End are
  local midnight of `date` and of the exclusive end `date`.
- `static class CalendarFeed`:
  - `string CalendarListUrl(string? pageToken)`
  - `string EventsUrl(string calendarId, DateTimeOffset from, DateTimeOffset to, string? pageToken)`: the calendar id
    is URL-escaped, with `singleEvents=true&orderBy=startTime&maxResults=250` and a `fields=` filter.
  - `(IReadOnlyList<string> Ids, string? Next) ParseCalendarList(string json)`: keeps `selected == true && !deleted`.
  - `(IReadOnlyList<CalEvent> Events, string? Next) ParseEvents(string json, TimeZoneInfo zone)`: skips
    `status == cancelled` and items without a start. An untitled event gets the title "(No title)".
  - `(DateTimeOffset From, DateTimeOffset To) Window(DateTimeOffset now)`
  - `IReadOnlySet<DateOnly> DaysWithEvents(IEnumerable<CalEvent> events, TimeZoneInfo zone)`
  - `CalEvent? NextEvent(IEnumerable<CalEvent> events, DateTimeOffset now)`
  - `string WhenLabel(CalEvent e, DateTimeOffset now, TimeZoneInfo zone)`: "TODAY 14:30", "TODAY" (all-day),
    "NOW" (in progress), "TOMORROW 09:00", "MON 24".

- [ ] Tests (zone = UTC unless stated):
  - `ParseCalendarList_KeepsSelectedOnly`: ids a (selected), b (not), c (selected, deleted) → [a].
  - `ParseCalendarList_ReadsNextPageToken`.
  - `ParseEvents_TimedAndAllDay`.
  - `ParseEvents_SkipsCancelled`.
  - `ParseEvents_Garbage_IsEmpty`.
  - `DaysWithEvents_AllDayEndIsExclusive`: 2026-11-30 to 2026-12-01 → {Nov 30}.
  - `DaysWithEvents_TimedAcrossMidnight`: 23:00 to 01:00 → two days.
  - `DaysWithEvents_TimedEndingAtMidnight`: 22:00 to 00:00 → one day.
  - `NextEvent_SkipsEnded_IncludesInProgress`.
  - `WhenLabel_TodayTomorrowLater`.
  - `Window_FromWeekBeforeMonthStart_To60DaysAhead`.
  - `EventsUrl_EscapesCalendarId`: `a#b@group` is escaped.
- [ ] Red, green. Commit `feat(calendar): Google Calendar feed rules`.

### Task 3: Vault, loopback sign-in, service (app)

**Files:**
- Create `src/NothingShell/GoogleCalendar/GoogleVault.cs`, `GoogleSignIn.cs`, `GoogleCalendarService.cs`.
- Test `tests/NothingShell.Tests/GoogleVaultTests.cs`.

**Interfaces — Produces:**
- `record GoogleCredentials(string ClientId, string ClientSecret, string? RefreshToken)`
- `sealed class GoogleVault(string dir)`:
  - `GoogleCredentials? Load()`
  - `void Save(GoogleCredentials)`
  - `IReadOnlyList<CalEvent>? LoadEvents()`
  - `void SaveEvents(IReadOnlyList<CalEvent>)`
  - `void Clear()`
  - Each file is a DPAPI-protected UTF-8 JSON blob with the entropy `"NothingShell.Google"`. Anything unreadable gives null.
- `static class GoogleSignIn`: `Task<GoogleToken?> RunAsync(HttpClient http, string clientId, string secret, CancellationToken ct)`.
  - It uses `TcpListener(IPAddress.Loopback, 0)`, opens the browser with `UnelevatedShell.Open(url, null)` and
    reads request lines until one carries a `code` or an `error` with the matching `state`.
  - It answers every request with a small HTML page, then exchanges the code.
  - Timeout: 5 minutes.
- `sealed class GoogleCalendarService : IDisposable` (ctor `(string dataDir)`):
  - `CalendarStatus Status` (`NotSetUp`, `SignedOut`, `SignedIn`, `Busy`)
  - `IReadOnlyList<CalEvent> Events`
  - `string StatusText`
  - `event Action? Changed` (UI thread)
  - `Task<bool> ConnectAsync(string clientId, string secret)`
  - `Task RefreshAsync()`
  - `void RefreshSoon()`: at most once per 2 min
  - `Task DisconnectAsync()`
  - `void Start()`

- [ ] Tests:
  - `Vault_RoundTripsCredentials`, `Vault_RoundTripsEvents`.
  - `Vault_CorruptFile_IsNull`.
  - `Vault_PlainJsonFile_IsNull`: the file isn't DPAPI.
  - `Vault_Clear_RemovesBoth`.
- [ ] Red, green, build the app project. Commit `feat(calendar): DPAPI vault, loopback sign-in, calendar service`.

### Task 4: Settings section, widget and wiring

**Files:** Modify `src/NothingShell/Settings/SettingsWindow.cs`, `src/NothingShell/Widgets/CalendarWidget.cs`,
`src/NothingShell/App.xaml.cs`, `README.md`, `build/verify/plan1-handson.ps1`.

- [ ] Settings, a "Google Calendar" section (after Weather):
  - When not signed in: the four guide steps from the spec, an "Open Google Cloud Console" link
    (`https://console.cloud.google.com/apis/credentials`, opened with `UnelevatedShell`), a Client ID `TextBox`, a
    Client secret `PasswordBox`, and a Connect pill.
  - When signed in: the status line, plus Refresh and Disconnect pills.
  - It repaints on `Changed`. `ShowOrActivate` gets an optional `GoogleCalendarService? calendar`.
- [ ] Widget `CalendarWidget(GoogleCalendarService? calendar, Func<bool> editing)`:
  - 2×2: a 4 px dot under days in `DaysWithEvents` (white on the red today circle, otherwise `TileChrome.Text`).
  - 2×1: the right half shows the next event (the `WhenLabel` label, then the title at most 2 lines, trimmed), then
    the month. With no event: "No events".
  - Not signed in and `editing()`: 2×2 shows a "CONNECT GOOGLE CALENDAR IN SETTINGS" label at the bottom; 2×1 shows
    it in place of "No events".
  - `Build` and `SetPaused(false)` call `RefreshSoon()`. `Changed` triggers a `Fill()`. `Dispose` unsubscribes.
- [ ] App: create the service in `StartLauncher` next to weather, guarded, and `Start()` it. Pass it to the widget
  factory and to Settings. Dispose it on exit.
- [ ] README and a hands-on `Calendar` check (connect, dots, next event, disconnect).
- [ ] Build, then run the full suite: `dotnet test NotWin.sln`, all passing. Commit `feat(calendar): Google Calendar in Settings and on the widget`.
