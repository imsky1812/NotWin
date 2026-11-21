# Plan 6: Google Calendar on the Calendar widget (short design)

**Why:** the parent spec (§6.5) chose the user's Google Calendar as the Calendar widget's source. Plan 5 shipped the
widget with dates only and deferred the sign-in to this plan (user decision 2026-11-19).

**What the user decided:**
- Events come from **every calendar switched on in Google Calendar** (`selected` in the calendar list), holidays and
  birthdays included, so the widget matches the phone (user, 2026-11-21).
- Sign-in uses the user's own Google Cloud OAuth client, with a guided setup in Settings. The client ID and secret are
  typed into Settings only and never pasted into chat (user, Plan 5).
- "Go ahead" on the builder's picks below (user, 2026-11-21).

**Parent spec rules kept (§6.5):**
- A one-time sign-in with the desktop-app OAuth flow and the **read-only** scope `calendar.readonly`.
- Tokens are stored encrypted for the current Windows user (DPAPI).
- Events refresh every 15 minutes and when the widget becomes visible.
- Not signed in: plain dates, plus a "Connect Google Calendar" hint in edit mode only.
- Offline: the last cached data is shown.

## Goals
- 2×2: a small dot under each day of the shown grid that has at least one event.
- 2×1: the right half shows the next event: its title (at most 2 lines), then "TODAY 14:30", "TOMORROW" or "MON 24".
  With no upcoming event in the next 60 days it shows "No events".
- 1×1: unchanged.
- Settings has a "Google Calendar" section with four short setup steps and a button that opens Google Cloud Console.
  It has a Client ID box and a Client secret box (masked), and Connect, Refresh and Disconnect buttons with a status line.

## Non-goals
- Creating or editing events.
- Choosing calendars in Settings.
- Event colours.
- More than one Google account.
- Notifications for events.

## Anti-goals
- **Never logged:** event titles, calendar names or ids, the account's email, tokens, the client ID or secret. The log
  gets counts and outcomes only.
- No new NuGet packages, so nothing new is downloaded.
- A Google failure must never take the widget or the shell down. The widget falls back to plain dates.

## Approaches considered
1. **Hand-rolled OAuth and REST over `HttpClient` (picked).** The weather service already makes web requests this way,
   DPAPI comes with the Windows Desktop runtime, and it needs no downloads.
2. **The Google.Apis.Calendar NuGet package.** Less code, but it is a download plus a large set of dependencies, for
   three endpoints.
3. **Reading Windows' own Calendar app or an ICS secret link.** No sign-in, but the user chose Google sign-in, and ICS
   links expose the calendar to anyone holding the URL.

## How it works (the builder's calls, user said go ahead)

**Sign-in**
- The desktop flow with PKCE (S256), a random `state`, and `access_type=offline&prompt=consent` so a refresh token
  always comes back.
- The redirect goes to `http://127.0.0.1:<free port>/`. The app uses a raw `TcpListener` on loopback rather than
  `HttpListener`, so no URL reservation is needed.
- It waits at most 5 minutes, then answers the browser with a short "You can close this tab" page.
- The browser opens unelevated through Explorer (`UnelevatedShell.Open`).

**Storage**
- `%AppData%\NothingShell\google.dat` holds the client ID, the client secret and the refresh token as one JSON blob,
  DPAPI-encrypted for the current user.
- `calendar.dat` holds the cached events, DPAPI-encrypted because titles are private.
- The access token lives in memory only.

**Fetch**
- The calendar list is fetched with only the needed fields. Calendars with `selected == true` and not deleted are kept.
- Events come from each of those calendars with `singleEvents=true`, from 7 days before the 1st of the current month
  to 60 days after now. Cancelled events are skipped.
- Every request follows `nextPageToken`.
- An expired access token is refreshed with the refresh token.
- `invalid_grant` means the user is signed out: the refresh token and the cache are deleted, and the status says
  "SIGNED OUT, CONNECT AGAIN".

**Refresh**
- Every 15 minutes.
- When a calendar widget is built (startup, edit mode, reattach) or resumes after full screen, it asks for a refresh,
  but no more often than once every 2 minutes.

**Dots and the next event**
- An all-day event covers its `date` up to its end date (exclusive). A timed event covers each local day from its
  start to just before its end.
- The next event is the one with the earliest start among events that have not ended yet. Events in progress count.

**Disconnect** revokes the token at `https://oauth2.googleapis.com/revoke` (best effort), then deletes `google.dat`
and `calendar.dat`.

**Guide text** (Settings):
1. Open Google Cloud Console and create a project.
2. In APIs & Services, enable the Google Calendar API.
3. On the OAuth consent screen, choose External, add yourself as a test user, then click Publish app. In Testing mode
   Google signs you out every 7 days.
4. Under Credentials, create an OAuth client ID of type Desktop app, and paste its ID and secret below.

When signing in, Google warns "Google hasn't verified this app". It's your own app: click Advanced, then Continue.

## Left to the builder
- Exact dot size and placement, hint wording, and status strings, in the existing `TileChrome` style.
